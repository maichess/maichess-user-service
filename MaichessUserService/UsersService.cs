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
}
