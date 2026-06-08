using System.Text.Json;
using Avro.Generic;
using MaichessUserService.Kafka;
using Xunit;

namespace MaichessUserService.Tests.Kafka;

// Unit tests for the CDC -> user.events transform. Representative Postgres change rows
// (insert/snapshot, and updates of rating, stats, username, dev_mode) must map to the
// correct user.events envelopes + payloads. See feature-prompts/10.
public sealed class CdcUserEventMapperTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";

    private readonly CdcUserEventMapper mapper = CdcUserEventMapper.FromEmbeddedSchema();

    [Theory]
    [InlineData("c")]
    [InlineData("r")]
    public void Insert_MapsToUserRegistered(string op)
    {
        IReadOnlyList<GenericRecord> events = mapper.Map(Change(op, before: null, after: Row(username: "alice")));

        GenericRecord env = Assert.Single(events);
        Assert.Equal("user.UserRegistered", env["event_type"]);
        Assert.Equal(Id, env["aggregate_id"]);
        Assert.Equal("user-cdc-relay", env["producer"]);
        Assert.Equal(string.Empty, env["correlation_id"]);
        Assert.Equal(string.Empty, env["causation_id"]);
        Assert.Equal(42L, env["sequence"]);
        Assert.Equal(1700L, env["occurred_at"]);

        var payload = (GenericRecord)env["payload"];
        Assert.Equal(Id, payload["user_id"]);
        Assert.Equal("alice", payload["username"]);
    }

    [Fact]
    public void UpdateUsername_MapsToProfileUpdatedOnly()
    {
        IReadOnlyList<GenericRecord> events = mapper.Map(Change(
            "u",
            before: Row(username: "alice"),
            after: Row(username: "bob")));

        GenericRecord env = Assert.Single(events);
        Assert.Equal("user.ProfileUpdated", env["event_type"]);
        var payload = (GenericRecord)env["payload"];
        Assert.Equal(Id, payload["user_id"]);
        Assert.Equal("bob", payload["username"]);
        Assert.Equal(false, payload["dev_mode"]);
    }

    [Fact]
    public void UpdateDevMode_MapsToProfileUpdatedOnly()
    {
        IReadOnlyList<GenericRecord> events = mapper.Map(Change(
            "u",
            before: Row(devMode: false),
            after: Row(devMode: true)));

        GenericRecord env = Assert.Single(events);
        Assert.Equal("user.ProfileUpdated", env["event_type"]);
        Assert.Equal(true, ((GenericRecord)env["payload"])["dev_mode"]);
    }

    [Fact]
    public void RecordMatchResult_MapsToRatingUpdatedOnly()
    {
        // A match result changes rating fields and a stat counter in one row write.
        IReadOnlyList<GenericRecord> events = mapper.Map(Change(
            "u",
            before: Row(rating: 400, rd: 350, vol: 0.06, elo: 400, wins: 0),
            after: Row(rating: 412.3, rd: 290.1, vol: 0.0599, elo: 412, wins: 1)));

        GenericRecord env = Assert.Single(events);
        Assert.Equal("user.RatingUpdated", env["event_type"]);
        var payload = (GenericRecord)env["payload"];
        Assert.Equal(Id, payload["user_id"]);
        Assert.Equal(412.3, payload["rating"]);
        Assert.Equal(290.1, payload["rating_deviation"]);
        Assert.Equal(0.0599, payload["volatility"]);
        Assert.Equal(412, payload["elo"]);
    }

    [Theory]
    [InlineData(410.0, 350.0, 0.06, 400)] // rating only
    [InlineData(400.0, 300.0, 0.06, 400)] // rating_deviation only
    [InlineData(400.0, 350.0, 0.05, 400)] // volatility only
    [InlineData(400.0, 350.0, 0.06, 401)] // elo only
    public void RatingFieldChange_EachTriggersRatingUpdated(double rating, double rd, double vol, int elo)
    {
        IReadOnlyList<GenericRecord> events = mapper.Map(Change(
            "u",
            before: Row(rating: 400, rd: 350, vol: 0.06, elo: 400),
            after: Row(rating: rating, rd: rd, vol: vol, elo: elo)));

        GenericRecord env = Assert.Single(events);
        Assert.Equal("user.RatingUpdated", env["event_type"]);
    }

    [Fact]
    public void UpdateProfileAndRating_MapsToBothEvents()
    {
        IReadOnlyList<GenericRecord> events = mapper.Map(Change(
            "u",
            before: Row(username: "alice", rating: 400),
            after: Row(username: "bob", rating: 450)));

        Assert.Equal(2, events.Count);
        Assert.Equal("user.ProfileUpdated", events[0]["event_type"]);
        Assert.Equal("user.RatingUpdated", events[1]["event_type"]);
    }

    [Fact]
    public void UpdateWithNoRelevantChange_MapsToNothing()
    {
        // Only password_hash differs — not a user.events fact.
        Dictionary<string, object?> before = Row();
        Dictionary<string, object?> after = Row();
        after["password_hash"] = "rotated";

        Assert.Empty(mapper.Map(Change("u", before, after)));
    }

    [Fact]
    public void UpdateWithoutBeforeImage_DegradesToBothEvents()
    {
        // Default REPLICA IDENTITY: Postgres ships no before-image. Emit current state.
        IReadOnlyList<GenericRecord> events = mapper.Map(Change("u", before: null, after: Row()));

        Assert.Equal(2, events.Count);
        Assert.Equal("user.ProfileUpdated", events[0]["event_type"]);
        Assert.Equal("user.RatingUpdated", events[1]["event_type"]);
    }

    [Fact]
    public void Delete_MapsToNothing()
    {
        Assert.Empty(mapper.Map(Change("d", before: Row(), after: null)));
    }

    [Fact]
    public void UnknownOp_MapsToNothing()
    {
        Assert.Empty(mapper.Map(Change("t", before: null, after: null)));
    }

    [Fact]
    public void MissingOp_MapsToNothing()
    {
        Assert.Empty(mapper.Map("""{ "after": { "id": "x" } }"""));
    }

    [Fact]
    public void InsertWithoutAfter_MapsToNothing()
    {
        Assert.Empty(mapper.Map(Change("c", before: null, after: null)));
    }

    [Fact]
    public void UpdateWithoutAfter_MapsToNothing()
    {
        Assert.Empty(mapper.Map(Change("u", before: Row(), after: null)));
    }

    [Fact]
    public void MissingFields_DefaultGracefully()
    {
        // after carries only the id (no username/dev_mode) — readers fall back to defaults.
        IReadOnlyList<GenericRecord> events = mapper.Map($$"""
            { "op": "c", "after": { "id": "{{Id}}" }, "source": { "lsn": 42 }, "ts_ms": 1700 }
            """);

        var payload = (GenericRecord)Assert.Single(events)["payload"];
        Assert.Equal(string.Empty, payload["username"]);
    }

    [Fact]
    public void WrappedSchemaEnvelope_IsUnwrapped()
    {
        // Debezium JSON converter with schemas enabled: { schema, payload: <change> }.
        string change = Change("c", before: null, after: Row(username: "alice"));
        string wrapped = $$"""{ "schema": { "type": "struct" }, "payload": {{change}} }""";

        GenericRecord env = Assert.Single(mapper.Map(wrapped));
        Assert.Equal("user.UserRegistered", env["event_type"]);
    }

    [Fact]
    public void SequenceDefaultsToZero_WhenLsnAbsent()
    {
        string change = $$"""
            { "op": "c", "before": null, "after": {{JsonSerializer.Serialize(Row())}}, "source": {}, "ts_ms": 1700 }
            """;

        Assert.Equal(0L, Assert.Single(mapper.Map(change))["sequence"]);
    }

    [Fact]
    public void SequenceDefaultsToZero_WhenSourceAbsent()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "ts_ms": 1700 }
            """;

        Assert.Equal(0L, Assert.Single(mapper.Map(change))["sequence"]);
    }

    [Fact]
    public void OccurredAt_FallsBackToSourceTsMs()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "source": { "lsn": 42, "ts_ms": 555 } }
            """;

        Assert.Equal(555L, Assert.Single(mapper.Map(change))["occurred_at"]);
    }

    [Fact]
    public void OccurredAt_DefaultsToZero_WhenNoTimestamp()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "source": { "lsn": 42 } }
            """;

        Assert.Equal(0L, Assert.Single(mapper.Map(change))["occurred_at"]);
    }

    [Fact]
    public void NonObjectRoot_MapsToNothing()
    {
        Assert.Empty(mapper.Map("[]"));
    }

    [Fact]
    public void UpdateNoBeforeWithMinimalAfter_DefaultsNumericFields()
    {
        // Degraded path (no before-image) + an after carrying only the id: the rating
        // event reads missing numeric columns as zero rather than throwing.
        IReadOnlyList<GenericRecord> events = mapper.Map($$"""
            { "op": "u", "before": null, "after": { "id": "{{Id}}" }, "source": { "lsn": 1 }, "ts_ms": 1 }
            """);

        var rating = (GenericRecord)events[1]["payload"];
        Assert.Equal("user.RatingUpdated", events[1]["event_type"]);
        Assert.Equal(0d, rating["rating"]);
        Assert.Equal(0, rating["elo"]);
    }

    [Fact]
    public void NonObjectSource_DefaultsSequenceAndTimestamp()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "source": 5 }
            """;

        GenericRecord env = Assert.Single(mapper.Map(change));
        Assert.Equal(0L, env["sequence"]);
        Assert.Equal(0L, env["occurred_at"]);
    }

    [Fact]
    public void NonNumericLsnAndTimestamp_DefaultToZero()
    {
        string change = $$"""
            { "op": "c", "after": {{JsonSerializer.Serialize(Row())}}, "source": { "lsn": "x", "ts_ms": "y" } }
            """;

        GenericRecord env = Assert.Single(mapper.Map(change));
        Assert.Equal(0L, env["sequence"]);
        Assert.Equal(0L, env["occurred_at"]);
    }

    [Fact]
    public void EventId_IsDeterministicAcrossReplays()
    {
        string change = Change("c", before: null, after: Row());

        string first = (string)Assert.Single(mapper.Map(change))["event_id"];
        string second = (string)Assert.Single(mapper.Map(change))["event_id"];

        Assert.Equal(first, second);
        Assert.True(Guid.TryParse(first, out _));
    }

    [Fact]
    public void EventId_DiffersPerEventTypeOnSameChange()
    {
        IReadOnlyList<GenericRecord> events = mapper.Map(Change(
            "u",
            before: Row(username: "alice", rating: 400),
            after: Row(username: "bob", rating: 450)));

        Assert.NotEqual((string)events[0]["event_id"], (string)events[1]["event_id"]);
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
