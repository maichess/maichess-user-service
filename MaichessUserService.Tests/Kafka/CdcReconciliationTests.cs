using System.Text.Json;
using Avro;
using Avro.Generic;
using MaichessUserService.Kafka;
using Xunit;

namespace MaichessUserService.Tests.Kafka;

// Reconciliation harness (feature-prompts/10 "Tests"): for each user-service operation,
// the CDC-derived user.events must agree with what an in-process ("legacy") emitter would
// have produced. The User service never actually shipped that emitter, so the reference
// below encodes its intended per-operation output (UserRegistered / ProfileUpdated /
// RatingUpdated). This is the side-by-side comparison the cutover relies on.
public sealed class CdcReconciliationTests
{
    private const string Id = "22222222-2222-2222-2222-222222222222";

    private readonly CdcUserEventMapper mapper = CdcUserEventMapper.FromEmbeddedSchema();

    [Fact]
    public void CreateUser_CdcMatchesLegacyEmitter()
    {
        var after = Row(username: "carol");

        AssertReconciles(
            legacy: [Expected("user.UserRegistered", new() { ["user_id"] = Id, ["username"] = "carol" })],
            cdc: mapper.Map(Change("c", before: null, after: after)));
    }

    [Fact]
    public void UpdateProfile_CdcMatchesLegacyEmitter()
    {
        var before = Row(username: "carol", devMode: false);
        var after = Row(username: "carol", devMode: true);

        AssertReconciles(
            legacy: [Expected("user.ProfileUpdated", new()
            {
                ["user_id"] = Id,
                ["username"] = "carol",
                ["dev_mode"] = true,
            })],
            cdc: mapper.Map(Change("u", before, after)));
    }

    [Fact]
    public void RecordMatchResult_CdcMatchesLegacyEmitter()
    {
        var before = Row(rating: 400, rd: 350, vol: 0.06, elo: 400, wins: 0);
        var after = Row(rating: 388.5, rd: 320.2, vol: 0.0601, elo: 389, losses: 1);

        AssertReconciles(
            legacy: [Expected("user.RatingUpdated", new()
            {
                ["user_id"] = Id,
                ["rating"] = 388.5,
                ["rating_deviation"] = 320.2,
                ["volatility"] = 0.0601,
                ["elo"] = 389,
            })],
            cdc: mapper.Map(Change("u", before, after)));
    }

    private static void AssertReconciles(
        IReadOnlyList<(string Type, Dictionary<string, object?> Fields)> legacy,
        IReadOnlyList<GenericRecord> cdc)
    {
        Assert.Equal(legacy.Count, cdc.Count);
        for (int i = 0; i < legacy.Count; i++)
        {
            Assert.Equal(legacy[i].Type, (string)cdc[i]["event_type"]);
            Assert.Equal(legacy[i].Fields, PayloadFields(cdc[i]));
        }
    }

    private static (string Type, Dictionary<string, object?> Fields) Expected(
        string type, Dictionary<string, object?> fields) => (type, fields);

    private static Dictionary<string, object?> PayloadFields(GenericRecord envelope)
    {
        var payload = (GenericRecord)envelope["payload"];
        var schema = (RecordSchema)payload.Schema;
        return schema.Fields.ToDictionary(f => f.Name, f => (object?)payload[f.Name]);
    }

    private static Dictionary<string, object?> Row(
        string username = "carol",
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
            ["source"] = new Dictionary<string, object?> { ["lsn"] = 7L, ["ts_ms"] = 1L },
            ["ts_ms"] = 2L,
        };
        return JsonSerializer.Serialize(root);
    }
}
