using System.Text.Json;
using Maichess.Events.V1;
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
    [Fact]
    public void CreateUser_CdcMatchesLegacyEmitter()
    {
        var after = Row(username: "carol");

        IReadOnlyList<UserEvent> cdc = CdcUserEventMapper.Map(Change("c", before: null, after: after));

        UserEvent ev = Assert.Single(cdc);
        Assert.Equal("user.UserRegistered", ev.EventType);
        Assert.Equal(Id, ev.UserRegistered.UserId);
        Assert.Equal("carol", ev.UserRegistered.Username);
    }

    [Fact]
    public void UpdateProfile_CdcMatchesLegacyEmitter()
    {
        var before = Row(username: "carol", devMode: false);
        var after = Row(username: "carol", devMode: true);

        IReadOnlyList<UserEvent> cdc = CdcUserEventMapper.Map(Change("u", before, after));

        UserEvent ev = Assert.Single(cdc);
        Assert.Equal("user.ProfileUpdated", ev.EventType);
        Assert.Equal(Id, ev.ProfileUpdated.UserId);
        Assert.Equal("carol", ev.ProfileUpdated.Username);
        Assert.True(ev.ProfileUpdated.DevMode);
    }

    [Fact]
    public void RecordMatchResult_CdcMatchesLegacyEmitter()
    {
        var before = Row(rating: 400, rd: 350, vol: 0.06, elo: 400, wins: 0);
        var after = Row(rating: 388.5, rd: 320.2, vol: 0.0601, elo: 389, losses: 1);

        IReadOnlyList<UserEvent> cdc = CdcUserEventMapper.Map(Change("u", before, after));

        UserEvent ev = Assert.Single(cdc);
        Assert.Equal("user.RatingUpdated", ev.EventType);
        RatingUpdated r = ev.RatingUpdated;
        Assert.Equal(Id, r.UserId);
        Assert.Equal(388.5, r.Rating);
        Assert.Equal(320.2, r.RatingDeviation);
        Assert.Equal(0.0601, r.Volatility);
        Assert.Equal(389, r.Elo);
        Assert.Equal(0, r.Wins);
        Assert.Equal(1, r.Losses);
        Assert.Equal(0, r.Draws);
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
