using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using Maichess.User.V1;
using MaichessUserService.Rating;
using ProtoUser = Maichess.User.V1.User;

namespace MaichessUserService;

internal sealed class UsersService(Database.DatabaseClient db)
{
    private const string Collection = "users";
    private const string RatedMatchesField = "rated_matches";

    // How many recently rated match ids are kept per row as the idempotency marker.
    // Sized to cover any realistic redelivery window; offsets remain the primary
    // guarantee, so a replay deeper than the cap is a deliberate operator action.
    private const int RatedMatchesCap = 64;

    // Bots are treated as having an established rating: the engine-configured elo
    // with a fixed low deviation (moved from match-manager's retired gRPC fan-out).
    private const double BotRatingDeviation = 50.0;

    internal async Task<CreateUserResult> CreateUserAsync(string username, string passwordHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new CreateUserResult.InvalidInput("username is required");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return new CreateUserResult.InvalidInput("password_hash is required");
        }

        Struct record = new();
        record.Fields["username"] = Value.ForString(username);
        record.Fields["password_hash"] = Value.ForString(passwordHash);
        record.Fields["elo"] = Value.ForNumber(Glicko2.DefaultRating);
        record.Fields["wins"] = Value.ForNumber(0);
        record.Fields["losses"] = Value.ForNumber(0);
        record.Fields["draws"] = Value.ForNumber(0);
        record.Fields["dev_mode"] = Value.ForBool(false);
        record.Fields["rating"] = Value.ForNumber(Glicko2.DefaultRating);
        record.Fields["rating_deviation"] = Value.ForNumber(Glicko2.DefaultRatingDeviation);
        record.Fields["volatility"] = Value.ForNumber(Glicko2.DefaultVolatility);

        try
        {
            InsertResponse response = await db.InsertAsync(
                new InsertRequest { Collection = Collection, Record = record },
                cancellationToken: ct);
            return new CreateUserResult.Success(UserFromStruct(response.Record));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            return new CreateUserResult.Conflict();
        }
    }

    internal async Task<GetUserResult> GetUserAsync(string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out _))
        {
            return new GetUserResult.InvalidUserId();
        }

        try
        {
            GetResponse response = await db.GetAsync(
                new GetRequest { Collection = Collection, Id = userId },
                cancellationToken: ct);
            return new GetUserResult.Success(UserFromStruct(response.Record));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return new GetUserResult.NotFound();
        }
    }

    internal async Task<UpdateUserResult> UpdateUserAsync(
        string userId, string? username, bool? devMode, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out _))
        {
            return new UpdateUserResult.InvalidInput("user_id must be a valid UUID");
        }

        if (username is not null && string.IsNullOrWhiteSpace(username))
        {
            return new UpdateUserResult.InvalidInput("username is required");
        }

        if (username is null && devMode is null)
        {
            return new UpdateUserResult.InvalidInput("at least one of username or dev_mode is required");
        }

        Struct fields = new();
        if (username is not null)
        {
            fields.Fields["username"] = Value.ForString(username);
        }

        if (devMode is not null)
        {
            fields.Fields["dev_mode"] = Value.ForBool(devMode.Value);
        }

        try
        {
            UpdateResponse response = await db.UpdateAsync(
                new UpdateRequest { Collection = Collection, Id = userId, Fields = fields },
                cancellationToken: ct);
            return new UpdateUserResult.Success(UserFromStruct(response.Record));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return new UpdateUserResult.NotFound();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            return new UpdateUserResult.Conflict();
        }
    }

    internal async Task<RecordMatchResultResult> RecordMatchResultAsync(
        string userId, MatchOutcome outcome, double opponentRating, double opponentRd, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out _))
        {
            return new RecordMatchResultResult.InvalidInput("user_id must be a valid UUID");
        }

        if (outcome is not (MatchOutcome.Win or MatchOutcome.Loss or MatchOutcome.Draw))
        {
            return new RecordMatchResultResult.InvalidInput("outcome is required");
        }

        try
        {
            GetResponse getResponse = await db.GetAsync(
                new GetRequest { Collection = Collection, Id = userId },
                cancellationToken: ct);
            ProtoUser user = UserFromStruct(getResponse.Record);
            Struct fields = ResultFields(user, outcome, opponentRating, opponentRd);

            UpdateResponse updateResponse = await db.UpdateAsync(
                new UpdateRequest { Collection = Collection, Id = userId, Fields = fields },
                cancellationToken: ct);
            return new RecordMatchResultResult.Success(UserFromStruct(updateResponse.Record));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return new RecordMatchResultResult.NotFound();
        }
    }

    // Applies a finished match to player stats and Glicko-2 ratings — the event-driven
    // successor to RecordMatchResult (kafka task 08). Exactly one rating mutation per
    // human participant per match: the per-user rated_matches marker commits in the same
    // row update as the rating it guards, so a redelivered or replayed MatchEnded never
    // double-counts. Bot-vs-bot matches have no human side and record nothing; external
    // matches are unrated by rule. Both human rows are loaded before either is updated
    // so each side is rated against the opponent's pre-match state, never one this same
    // fan-out already moved. A missing human row skips every side that depends on it
    // (mirroring the retired gRPC path, where the lookup failure recorded nothing).
    // Returns the user ids actually updated.
    internal async Task<IReadOnlyList<string>> ApplyMatchEndedAsync(MatchEndedFact fact, CancellationToken ct)
    {
        if (fact.External
            || fact.Status == MatchEndedStatus.Unknown
            || string.IsNullOrWhiteSpace(fact.MatchId)
            || (fact.White.UserId is not null && fact.White.UserId == fact.Black.UserId))
        {
            return [];
        }

        Struct? whiteRecord = await TryGetRecordAsync(fact.White.UserId, ct);
        Struct? blackRecord = await TryGetRecordAsync(fact.Black.UserId, ct);

        (MatchOutcome whiteOutcome, MatchOutcome blackOutcome) = fact.Status switch
        {
            MatchEndedStatus.WhiteWon => (MatchOutcome.Win, MatchOutcome.Loss),
            MatchEndedStatus.BlackWon => (MatchOutcome.Loss, MatchOutcome.Win),
            _ => (MatchOutcome.Draw, MatchOutcome.Draw),
        };

        var applied = new List<string>(2);
        if (await ApplyResultAsync(fact.MatchId, whiteRecord, whiteOutcome, OpponentOf(fact.Black, blackRecord), ct))
        {
            applied.Add(fact.White.UserId!);
        }

        if (await ApplyResultAsync(fact.MatchId, blackRecord, blackOutcome, OpponentOf(fact.White, whiteRecord), ct))
        {
            applied.Add(fact.Black.UserId!);
        }

        return applied;
    }

    // The opponent's pre-match (rating, deviation): a human's snapshotted row, or a
    // bot's engine elo with the fixed established-bot deviation (unknown bot -> 0).
    // Null when a human opponent's row is missing — the dependent side is skipped
    // rather than rated against an invented state.
    private static (double Rating, double Rd)? OpponentOf(MatchEndedParticipant opponent, Struct? record)
    {
        if (opponent.UserId is null)
        {
            return (opponent.BotElo ?? 0.0, BotRatingDeviation);
        }

        if (record is null)
        {
            return null;
        }

        ProtoUser user = UserFromStruct(record);
        return (user.Rating, user.RatingDeviation);
    }

    // The row's recently rated match ids. Rows from before the column existed, or a
    // malformed value, read as empty — the dedupe then fails open to applying, which
    // matches the consumer's at-least-once semantics.
    private static List<string> RatedMatches(Struct record)
    {
        if (!record.Fields.TryGetValue(RatedMatchesField, out Value? value)
            || value.KindCase != Value.KindOneofCase.StringValue)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value.StringValue) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Struct ResultFields(ProtoUser user, MatchOutcome outcome, double opponentRating, double opponentRd)
    {
        (string field, int newValue) = outcome switch
        {
            MatchOutcome.Win => ("wins", user.Wins + 1),
            MatchOutcome.Loss => ("losses", user.Losses + 1),
            _ => ("draws", user.Draws + 1),
        };

        double score = outcome switch
        {
            MatchOutcome.Win => 1.0,
            MatchOutcome.Loss => 0.0,
            _ => 0.5,
        };

        RatingState updated = Glicko2.Update(
            new RatingState(user.Rating, user.RatingDeviation, user.Volatility),
            [new RatingGame(opponentRating, opponentRd, score)]);

        Struct fields = new();
        fields.Fields[field] = Value.ForNumber(newValue);
        fields.Fields["rating"] = Value.ForNumber(updated.Rating);
        fields.Fields["rating_deviation"] = Value.ForNumber(updated.RatingDeviation);
        fields.Fields["volatility"] = Value.ForNumber(updated.Volatility);
        fields.Fields["elo"] = Value.ForNumber(Math.Round(updated.Rating));
        return fields;
    }

    private static ProtoUser UserFromStruct(Struct s)
    {
        int elo = (int)s.Fields["elo"].NumberValue;

        // Records written before the Glicko-2 fields existed fall back to a state
        // derived from their stored elo, mirroring the legacy dev_mode handling.
        double rating = s.Fields.TryGetValue("rating", out Value? r) ? r.NumberValue : elo;
        double ratingDeviation = s.Fields.TryGetValue("rating_deviation", out Value? rd)
            ? rd.NumberValue
            : Glicko2.DefaultRatingDeviation;
        double volatility = s.Fields.TryGetValue("volatility", out Value? vol)
            ? vol.NumberValue
            : Glicko2.DefaultVolatility;

        return new ProtoUser
        {
            Id = s.Fields["id"].StringValue,
            Username = s.Fields["username"].StringValue,
            Elo = elo,
            Wins = (int)s.Fields["wins"].NumberValue,
            Losses = (int)s.Fields["losses"].NumberValue,
            Draws = (int)s.Fields["draws"].NumberValue,
            DevMode = s.Fields.TryGetValue("dev_mode", out Value? devMode) && devMode.BoolValue,
            Rating = rating,
            RatingDeviation = ratingDeviation,
            Volatility = volatility,
        };
    }

    private async Task<Struct?> TryGetRecordAsync(string? userId, CancellationToken ct)
    {
        if (userId is null)
        {
            return null;
        }

        try
        {
            GetResponse response = await db.GetAsync(
                new GetRequest { Collection = Collection, Id = userId },
                cancellationToken: ct);
            return response.Record;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<bool> ApplyResultAsync(
        string matchId,
        Struct? record,
        MatchOutcome outcome,
        (double Rating, double Rd)? opponent,
        CancellationToken ct)
    {
        if (record is null || opponent is null)
        {
            return false;
        }

        List<string> ratedMatches = RatedMatches(record);
        if (ratedMatches.Contains(matchId))
        {
            return false;
        }

        ProtoUser user = UserFromStruct(record);
        Struct fields = ResultFields(user, outcome, opponent.Value.Rating, opponent.Value.Rd);
        ratedMatches.Insert(0, matchId);
        fields.Fields[RatedMatchesField] = Value.ForString(
            JsonSerializer.Serialize(ratedMatches.Take(RatedMatchesCap)));

        try
        {
            await db.UpdateAsync(
                new UpdateRequest { Collection = Collection, Id = user.Id, Fields = fields },
                cancellationToken: ct);
            return true;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return false;
        }
    }
}
