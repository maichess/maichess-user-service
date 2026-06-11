using System.Text.Json;
using Maichess.Events.V1;
using MaichessUserService.Kafka;
using Xunit;

namespace MaichessUserService.Tests.Kafka;

// Unit tests for the CDC -> user.events transform. Representative Postgres change rows
// (insert/snapshot, and updates of rating, stats, username, dev_mode) must map to the
// correct user.events envelopes + payloads. See feature-prompts/10.
public sealed class CdcUserEventMapperTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    [Theory]
    [InlineData("c")]
    [InlineData("r")]
    public void Insert_MapsToUserRegistered(string op)
    {
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map(Change(op, before: null, after: Row(username: "alice")));

        UserEvent env = Assert.Single(events);
        Assert.Equal("user.UserRegistered", env.EventType);
        Assert.Equal(Id, env.AggregateId);
        Assert.Equal("user-cdc-relay", env.Producer);
        Assert.Equal(string.Empty, env.CorrelationId);
        Assert.Equal(string.Empty, env.CausationId);
        Assert.Equal(42L, env.Sequence);
        Assert.Equal(1700L, env.OccurredAt);

        Assert.Equal(Id, env.UserRegistered.UserId);
        Assert.Equal("alice", env.UserRegistered.Username);
    }

    [Fact]
    public void UpdateUsername_MapsToProfileUpdatedOnly()
    {
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map(Change(
            "u",
            before: Row(username: "alice"),
            after: Row(username: "bob")));

        UserEvent env = Assert.Single(events);
        Assert.Equal("user.ProfileUpdated", env.EventType);
        Assert.Equal(Id, env.ProfileUpdated.UserId);
        Assert.Equal("bob", env.ProfileUpdated.Username);
        Assert.False(env.ProfileUpdated.DevMode);
    }

    [Fact]
    public void UpdateDevMode_MapsToProfileUpdatedOnly()
    {
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map(Change(
            "u",
            before: Row(devMode: false),
            after: Row(devMode: true)));

        UserEvent env = Assert.Single(events);
        Assert.Equal("user.ProfileUpdated", env.EventType);
        Assert.True(env.ProfileUpdated.DevMode);
    }

    [Fact]
    public void RecordMatchResult_MapsToRatingUpdatedOnly()
    {
        // A match result changes rating fields and a stat counter in one row write.
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map(Change(
            "u",
            before: Row(rating: 400, rd: 350, vol: 0.06, elo: 400, wins: 0, losses: 2, draws: 1),
            after: Row(rating: 412.3, rd: 290.1, vol: 0.0599, elo: 412, wins: 1, losses: 2, draws: 1)));

        UserEvent env = Assert.Single(events);
        Assert.Equal("user.RatingUpdated", env.EventType);
        RatingUpdated payload = env.RatingUpdated;
        Assert.Equal(Id, payload.UserId);
        Assert.Equal(412.3, payload.Rating);
        Assert.Equal(290.1, payload.RatingDeviation);
        Assert.Equal(0.0599, payload.Volatility);
        Assert.Equal(412, payload.Elo);
        Assert.Equal(1, payload.Wins);
        Assert.Equal(2, payload.Losses);
        Assert.Equal(1, payload.Draws);
    }

    [Theory]
    [InlineData(410.0, 350.0, 0.06, 400)] // rating only
    [InlineData(400.0, 300.0, 0.06, 400)] // rating_deviation only
    [InlineData(400.0, 350.0, 0.05, 400)] // volatility only
    [InlineData(400.0, 350.0, 0.06, 401)] // elo only
    public void RatingFieldChange_EachTriggersRatingUpdated(double rating, double rd, double vol, int elo)
    {
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map(Change(
            "u",
            before: Row(rating: 400, rd: 350, vol: 0.06, elo: 400),
            after: Row(rating: rating, rd: rd, vol: vol, elo: elo)));

        Assert.Equal("user.RatingUpdated", Assert.Single(events).EventType);
    }

    [Theory]
    [InlineData(1, 0, 0)] // wins only
    [InlineData(0, 1, 0)] // losses only
    [InlineData(0, 0, 1)] // draws only
    public void StatCounterChange_EachTriggersRatingUpdated(int wins, int losses, int draws)
    {
        // A draw between evenly-matched players can leave rating fields unchanged while
        // a W/L/D counter ticks; the replica still needs the fresh tally.
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map(Change(
            "u",
            before: Row(wins: 0, losses: 0, draws: 0),
            after: Row(wins: wins, losses: losses, draws: draws)));

        Assert.Equal("user.RatingUpdated", Assert.Single(events).EventType);
    }

    [Fact]
    public void UpdateProfileAndRating_MapsToBothEvents()
    {
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map(Change(
            "u",
            before: Row(username: "alice", rating: 400),
            after: Row(username: "bob", rating: 450)));

        Assert.Equal(2, events.Count);
        Assert.Equal("user.ProfileUpdated", events[0].EventType);
        Assert.Equal("user.RatingUpdated", events[1].EventType);
    }

    [Fact]
    public void UpdateWithNoRelevantChange_MapsToNothing()
    {
        // Only password_hash differs — not a user.events fact.
        Dictionary<string, object?> before = Row();
        Dictionary<string, object?> after = Row();
        after["password_hash"] = "rotated";

        Assert.Empty(CdcUserEventMapper.Map(Change("u", before, after)));
    }

    [Fact]
    public void UpdateWithoutBeforeImage_DegradesToBothEvents()
    {
        // Default REPLICA IDENTITY: Postgres ships no before-image. Emit current state.
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map(Change("u", before: null, after: Row()));

        Assert.Equal(2, events.Count);
        Assert.Equal("user.ProfileUpdated", events[0].EventType);
        Assert.Equal("user.RatingUpdated", events[1].EventType);
    }

    [Fact]
    public void Delete_MapsToNothing()
    {
        Assert.Empty(CdcUserEventMapper.Map(Change("d", before: Row(), after: null)));
    }

    [Fact]
    public void UnknownOp_MapsToNothing()
    {
        Assert.Empty(CdcUserEventMapper.Map(Change("t", before: null, after: null)));
    }

    [Fact]
    public void MissingOp_MapsToNothing()
    {
        Assert.Empty(CdcUserEventMapper.Map("""{ "after": { "id": "x" } }"""));
    }

    [Fact]
    public void InsertWithoutAfter_MapsToNothing()
    {
        Assert.Empty(CdcUserEventMapper.Map(Change("c", before: null, after: null)));
    }

    [Fact]
    public void UpdateWithoutAfter_MapsToNothing()
    {
        Assert.Empty(CdcUserEventMapper.Map(Change("u", before: Row(), after: null)));
    }

    [Fact]
    public void MissingFields_DefaultGracefully()
    {
        // after carries only the id (no username/dev_mode) — readers fall back to defaults.
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map($$"""
            { "op": "c", "after": { "id": "{{Id}}" }, "source": { "lsn": 42 }, "ts_ms": 1700 }
            """);

        Assert.Equal(string.Empty, Assert.Single(events).UserRegistered.Username);
    }

    [Fact]
    public void WrappedSchemaEnvelope_IsUnwrapped()
    {
        // Debezium JSON converter with schemas enabled: { schema, payload: <change> }.
        string change = Change("c", before: null, after: Row(username: "alice"));
        string wrapped = $$"""{ "schema": { "type": "struct" }, "payload": {{change}} }""";

        UserEvent env = Assert.Single(CdcUserEventMapper.Map(wrapped));
        Assert.Equal("user.UserRegistered", env.EventType);
    }

    [Fact]
    public void SequenceDefaultsToZero_WhenLsnAbsent()
    {
        string change = $$"""
            { "op": "c", "before": null, "after": {{JsonSerializer.Serialize(Row())}}, "source": {}, "ts_ms": 1700 }
            """;

        Assert.Equal(0L, Assert.Single(CdcUserEventMapper.Map(change)).Sequence);
    }

    [Fact]
    public void SequenceDefaultsToZero_WhenSourceAbsent()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "ts_ms": 1700 }
            """;

        Assert.Equal(0L, Assert.Single(CdcUserEventMapper.Map(change)).Sequence);
    }

    [Fact]
    public void OccurredAt_FallsBackToSourceTsMs()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "source": { "lsn": 42, "ts_ms": 555 } }
            """;

        Assert.Equal(555L, Assert.Single(CdcUserEventMapper.Map(change)).OccurredAt);
    }

    [Fact]
    public void OccurredAt_DefaultsToZero_WhenNoTimestamp()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "source": { "lsn": 42 } }
            """;

        Assert.Equal(0L, Assert.Single(CdcUserEventMapper.Map(change)).OccurredAt);
    }

    [Fact]
    public void NonObjectRoot_MapsToNothing()
    {
        Assert.Empty(CdcUserEventMapper.Map("[]"));
    }

    [Fact]
    public void UpdateNoBeforeWithMinimalAfter_DefaultsNumericFields()
    {
        // Degraded path (no before-image) + an after carrying only the id: the rating
        // event reads missing numeric columns as zero rather than throwing.
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map($$"""
            { "op": "u", "before": null, "after": { "id": "{{Id}}" }, "source": { "lsn": 1 }, "ts_ms": 1 }
            """);

        Assert.Equal("user.RatingUpdated", events[1].EventType);
        Assert.Equal(0d, events[1].RatingUpdated.Rating);
        Assert.Equal(0, events[1].RatingUpdated.Elo);
    }

    [Fact]
    public void NonObjectSource_DefaultsSequenceAndTimestamp()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "source": 5 }
            """;

        UserEvent env = Assert.Single(CdcUserEventMapper.Map(change));
        Assert.Equal(0L, env.Sequence);
        Assert.Equal(0L, env.OccurredAt);
    }

    [Fact]
    public void NonNumericLsnAndTimestamp_DefaultToZero()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "source": { "lsn": "x", "ts_ms": "y" } }
            """;

        UserEvent env = Assert.Single(CdcUserEventMapper.Map(change));
        Assert.Equal(0L, env.Sequence);
        Assert.Equal(0L, env.OccurredAt);
    }

    [Fact]
    public void EventId_IsDeterministicAcrossReplays()
    {
        string change = Change("c", before: null, after: Row());

        string first = Assert.Single(CdcUserEventMapper.Map(change)).EventId;
        string second = Assert.Single(CdcUserEventMapper.Map(change)).EventId;

        Assert.Equal(first, second);
        Assert.True(Guid.TryParse(first, out _));
    }

    [Fact]
    public void EventId_DiffersPerEventTypeOnSameChange()
    {
        IReadOnlyList<UserEvent> events = CdcUserEventMapper.Map(Change(
            "u",
            before: Row(username: "alice", rating: 400),
            after: Row(username: "bob", rating: 450)));

        Assert.NotEqual(events[0].EventId, events[1].EventId);
    }

    private static Dictionary<string, object?> Row(
        string username = "alice",
        bool devMode = false,
        double rating = 400,
        double rd = 350,
        double vol = 0.06,
        int elo = 400,
        int wins = 0,
        int losses = 0,
        int draws = 0) => new()
    {
        ["id"] = Id,
        ["username"] = username,
        ["password_hash"] = "hash",
        ["elo"] = elo,
        ["wins"] = wins,
        ["losses"] = losses,
        ["draws"] = draws,
        ["dev_mode"] = devMode,
        ["rating"] = rating,
        ["rating_deviation"] = rd,
        ["volatility"] = vol,
    };

    private static string Change(string op, Dictionary<string, object?>? before, Dictionary<string, object?>? after)
    {
        var root = new Dictionary<string, object?>
        {
            ["op"] = op,
            ["before"] = before,
            ["after"] = after,
            ["source"] = new Dictionary<string, object?> { ["lsn"] = 42L, ["ts_ms"] = 999L },
            ["ts_ms"] = 1700L,
        };
        return JsonSerializer.Serialize(root);
    }
}
