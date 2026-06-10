using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using Maichess.User.V1;
using MaichessUserService.Rating;
using MaichessUserService.Tests.Support;
using NSubstitute;
using Xunit;

namespace MaichessUserService.Tests.Rating;

// The rating trigger + idempotency logic behind the match.events.v1 MatchEnded
// consumer (kafka task 08): exactly one rating mutation per human participant per
// finished match, opponents rated against pre-match snapshots, bot-vs-bot and
// external matches record nothing, redelivery/replay never double-counts.
public sealed class ApplyMatchEndedTests
{
    private const string MatchId = "99999999-9999-9999-9999-999999999999";
    private const string WhiteId = "11111111-1111-1111-1111-111111111111";
    private const string BlackId = "22222222-2222-2222-2222-222222222222";

    private readonly Database.DatabaseClient db = Substitute.For<Database.DatabaseClient>();
    private readonly List<UpdateRequest> updates = [];
    private readonly UsersService service;

    public ApplyMatchEndedTests()
    {
        service = new UsersService(db);

        db.GetAsync(
            Arg.Any<GetRequest>(),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns(_ => GrpcHelper.GrpcCallFailed<GetResponse>(StatusCode.NotFound));

        db.UpdateAsync(
            Arg.Any<UpdateRequest>(),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                UpdateRequest request = callInfo.Arg<UpdateRequest>();
                updates.Add(request);
                Struct record = request.Fields.Clone();
                record.Fields["id"] = Value.ForString(request.Id);
                return GrpcHelper.GrpcCall(new UpdateResponse { Record = record });
            });
    }

    [Fact]
    public async Task HumanVsHuman_RatesBothOnceAgainstPreMatchSnapshots()
    {
        // Fractional rating and non-default volatility so the stored Glicko-2
        // state — not the rounded elo or the seed fallbacks — provably drives
        // the update.
        SetRow(WhiteId, Row(WhiteId, rating: 500.3, rd: 200, volatility: 0.07, wins: 1));
        SetRow(BlackId, Row(BlackId, rating: 450.7, rd: 100, volatility: 0.05, losses: 2));

        IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(Fact(), CancellationToken.None);

        Assert.Equal([WhiteId, BlackId], applied);
        Assert.Equal(2, updates.Count);

        UpdateRequest white = updates.Single(u => u.Id == WhiteId);
        Assert.Equal(2, white.Fields.Fields["wins"].NumberValue);
        AssertRating(white, Expected(500.3, 200, 0.07, opponentRating: 450.7, opponentRd: 100, score: 1.0));
        Assert.Equal([MatchId], RatedMatchesOf(white));

        // Black is rated against white's pre-match 500.3/200 snapshot even though
        // white's row was already moved by this same fan-out.
        UpdateRequest black = updates.Single(u => u.Id == BlackId);
        Assert.Equal(3, black.Fields.Fields["losses"].NumberValue);
        AssertRating(black, Expected(450.7, 100, 0.05, opponentRating: 500.3, opponentRd: 200, score: 0.0));
        Assert.Equal([MatchId], RatedMatchesOf(black));
    }

    [Fact]
    public async Task BlackWon_RecordsLossForWhiteAndWinForBlack()
    {
        SetRow(WhiteId, Row(WhiteId, losses: 4));
        SetRow(BlackId, Row(BlackId, wins: 7));

        await service.ApplyMatchEndedAsync(Fact(MatchEndedStatus.BlackWon), CancellationToken.None);

        UpdateRequest white = updates.Single(u => u.Id == WhiteId);
        Assert.Equal(5, white.Fields.Fields["losses"].NumberValue);
        AssertRating(white, Expected(400, 350, 0.06, opponentRating: 400, opponentRd: 350, score: 0.0));

        UpdateRequest black = updates.Single(u => u.Id == BlackId);
        Assert.Equal(8, black.Fields.Fields["wins"].NumberValue);
        AssertRating(black, Expected(400, 350, 0.06, opponentRating: 400, opponentRd: 350, score: 1.0));
    }

    [Fact]
    public async Task Draw_IncrementsDrawsForBothWithHalfScore()
    {
        SetRow(WhiteId, Row(WhiteId, draws: 1));
        SetRow(BlackId, Row(BlackId));

        await service.ApplyMatchEndedAsync(Fact(MatchEndedStatus.Draw), CancellationToken.None);

        Assert.Equal(2, updates.Single(u => u.Id == WhiteId).Fields.Fields["draws"].NumberValue);
        Assert.Equal(1, updates.Single(u => u.Id == BlackId).Fields.Fields["draws"].NumberValue);
        AssertRating(
            updates.Single(u => u.Id == WhiteId),
            Expected(400, 350, 0.06, opponentRating: 400, opponentRd: 350, score: 0.5));
    }

    [Fact]
    public async Task Redelivery_AlreadyRatedMatchIsNotDoubleCounted()
    {
        string marker = JsonSerializer.Serialize(new[] { MatchId });
        SetRow(WhiteId, Row(WhiteId, ratedMatches: marker));
        SetRow(BlackId, Row(BlackId, ratedMatches: marker));

        IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(Fact(), CancellationToken.None);

        Assert.Empty(applied);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task PartialRedelivery_OnlyTheUnratedSideIsApplied()
    {
        SetRow(WhiteId, Row(WhiteId, ratedMatches: JsonSerializer.Serialize(new[] { MatchId })));
        SetRow(BlackId, Row(BlackId));

        IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(Fact(), CancellationToken.None);

        Assert.Equal([BlackId], applied);
        Assert.Equal(BlackId, Assert.Single(updates).Id);
    }

    [Fact]
    public async Task BotVsBot_RecordsNothing()
    {
        IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(
            Fact(whiteUser: null, whiteBotElo: 1200, blackUser: null, blackBotElo: 1500),
            CancellationToken.None);

        Assert.Empty(applied);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task HumanVsBot_RatesTheHumanAgainstTheBotElo()
    {
        SetRow(WhiteId, Row(WhiteId));

        IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(
            Fact(blackUser: null, blackBotElo: 1500),
            CancellationToken.None);

        Assert.Equal([WhiteId], applied);
        AssertRating(
            Assert.Single(updates),
            Expected(400, 350, 0.06, opponentRating: 1500, opponentRd: 50, score: 1.0));
    }

    [Fact]
    public async Task HumanVsBot_UnknownBotEloFallsBackToZero()
    {
        SetRow(WhiteId, Row(WhiteId));

        await service.ApplyMatchEndedAsync(Fact(blackUser: null), CancellationToken.None);

        AssertRating(
            Assert.Single(updates),
            Expected(400, 350, 0.06, opponentRating: 0, opponentRd: 50, score: 1.0));
    }

    [Fact]
    public async Task ExternalMatch_RecordsNothing()
    {
        SetRow(WhiteId, Row(WhiteId));
        SetRow(BlackId, Row(BlackId));

        IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(
            Fact(external: true),
            CancellationToken.None);

        Assert.Empty(applied);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task UnknownStatus_RecordsNothing()
    {
        SetRow(WhiteId, Row(WhiteId));
        SetRow(BlackId, Row(BlackId));

        Assert.Empty(await service.ApplyMatchEndedAsync(Fact(MatchEndedStatus.Unknown), CancellationToken.None));
        Assert.Empty(updates);
    }

    [Fact]
    public async Task EmptyMatchId_RecordsNothing()
    {
        Assert.Empty(await service.ApplyMatchEndedAsync(Fact(matchId: " "), CancellationToken.None));
        Assert.Empty(updates);
    }

    [Fact]
    public async Task SameUserOnBothSides_RecordsNothing()
    {
        SetRow(WhiteId, Row(WhiteId));

        Assert.Empty(await service.ApplyMatchEndedAsync(Fact(blackUser: WhiteId), CancellationToken.None));
        Assert.Empty(updates);
    }

    [Fact]
    public async Task MissingHumanRow_SkipsEverySideDependingOnIt()
    {
        // White's row does not exist: white cannot be rated, and black cannot be
        // rated against an invented opponent state, so nothing is recorded.
        SetRow(BlackId, Row(BlackId));

        IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(Fact(), CancellationToken.None);

        Assert.Empty(applied);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task RowDeletedBetweenSnapshotAndUpdate_SkipsThatSideOnly()
    {
        SetRow(WhiteId, Row(WhiteId));
        SetRow(BlackId, Row(BlackId));
        db.UpdateAsync(
            Arg.Is<UpdateRequest>(r => r.Id == WhiteId),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns(_ => GrpcHelper.GrpcCallFailed<UpdateResponse>(StatusCode.NotFound));

        IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(Fact(), CancellationToken.None);

        Assert.Equal([BlackId], applied);
        Assert.Equal(BlackId, Assert.Single(updates).Id);
    }

    [Fact]
    public async Task RatedMatches_NewestFirstAndCappedAtSixtyFour()
    {
        string[] existing = [.. Enumerable.Range(0, 64).Select(i => $"m{i}")];
        SetRow(WhiteId, Row(WhiteId, ratedMatches: JsonSerializer.Serialize(existing)));

        await service.ApplyMatchEndedAsync(Fact(blackUser: null, blackBotElo: 800), CancellationToken.None);

        List<string> rated = RatedMatchesOf(Assert.Single(updates));
        Assert.Equal(64, rated.Count);
        Assert.Equal(MatchId, rated[0]);
        Assert.Equal("m62", rated[^1]);
        Assert.DoesNotContain("m63", rated);
    }

    [Fact]
    public async Task MalformedRatedMatches_ReadsAsEmptyAndApplies()
    {
        SetRow(WhiteId, Row(WhiteId, ratedMatches: "not json"));

        IReadOnlyList<string> applied = await service.ApplyMatchEndedAsync(
            Fact(blackUser: null, blackBotElo: 800),
            CancellationToken.None);

        Assert.Equal([WhiteId], applied);
        Assert.Equal([MatchId], RatedMatchesOf(Assert.Single(updates)));
    }

    [Fact]
    public async Task JsonNullRatedMatches_ReadsAsEmptyAndApplies()
    {
        SetRow(WhiteId, Row(WhiteId, ratedMatches: "null"));

        Assert.Equal(
            [WhiteId],
            await service.ApplyMatchEndedAsync(Fact(blackUser: null, blackBotElo: 800), CancellationToken.None));
    }

    [Fact]
    public async Task NonStringRatedMatches_ReadsAsEmptyAndApplies()
    {
        Struct row = Row(WhiteId);
        row.Fields["rated_matches"] = Value.ForNumber(5);
        SetRow(WhiteId, row);

        Assert.Equal(
            [WhiteId],
            await service.ApplyMatchEndedAsync(Fact(blackUser: null, blackBotElo: 800), CancellationToken.None));
    }

    [Fact]
    public async Task LegacyRowWithoutRatedMatchesField_TreatedAsEmpty()
    {
        SetRow(WhiteId, Row(WhiteId, ratedMatches: null));

        await service.ApplyMatchEndedAsync(Fact(blackUser: null, blackBotElo: 800), CancellationToken.None);

        Assert.Equal([MatchId], RatedMatchesOf(Assert.Single(updates)));
    }

    private static MatchEndedFact Fact(
        MatchEndedStatus status = MatchEndedStatus.WhiteWon,
        string? whiteUser = WhiteId,
        string? blackUser = BlackId,
        double? whiteBotElo = null,
        double? blackBotElo = null,
        bool external = false,
        string matchId = MatchId) =>
        new(
            matchId,
            status,
            external,
            new MatchEndedParticipant(whiteUser, whiteBotElo),
            new MatchEndedParticipant(blackUser, blackBotElo));

    private static Struct Row(
        string id,
        double rating = 400,
        double rd = 350,
        double volatility = 0.06,
        int wins = 0,
        int losses = 0,
        int draws = 0,
        string? ratedMatches = "[]")
    {
        Struct row = new();
        row.Fields["id"] = Value.ForString(id);
        row.Fields["username"] = Value.ForString("player-" + id[..8]);
        row.Fields["elo"] = Value.ForNumber(Math.Round(rating));
        row.Fields["wins"] = Value.ForNumber(wins);
        row.Fields["losses"] = Value.ForNumber(losses);
        row.Fields["draws"] = Value.ForNumber(draws);
        row.Fields["rating"] = Value.ForNumber(rating);
        row.Fields["rating_deviation"] = Value.ForNumber(rd);
        row.Fields["volatility"] = Value.ForNumber(volatility);
        if (ratedMatches is not null)
        {
            row.Fields["rated_matches"] = Value.ForString(ratedMatches);
        }

        return row;
    }

    private static RatingState Expected(
        double rating,
        double rd,
        double volatility,
        double opponentRating,
        double opponentRd,
        double score) =>
        Glicko2.Update(
            new RatingState(rating, rd, volatility),
            [new RatingGame(opponentRating, opponentRd, score)]);

    private static void AssertRating(UpdateRequest update, RatingState expected)
    {
        Assert.Equal(expected.Rating, update.Fields.Fields["rating"].NumberValue);
        Assert.Equal(expected.RatingDeviation, update.Fields.Fields["rating_deviation"].NumberValue);
        Assert.Equal(expected.Volatility, update.Fields.Fields["volatility"].NumberValue);
        Assert.Equal(Math.Round(expected.Rating), update.Fields.Fields["elo"].NumberValue);
    }

    private static List<string> RatedMatchesOf(UpdateRequest update) =>
        JsonSerializer.Deserialize<List<string>>(update.Fields.Fields["rated_matches"].StringValue)!;

    private void SetRow(string id, Struct row) =>
        db.GetAsync(
            Arg.Is<GetRequest>(r => r.Id == id),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns(_ => GrpcHelper.GrpcCall(new GetResponse { Record = row }));
}
