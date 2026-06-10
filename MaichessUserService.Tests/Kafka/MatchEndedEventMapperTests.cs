using Maichess.Events.V1;
using MaichessUserService.Kafka;
using MaichessUserService.Rating;
using Xunit;

namespace MaichessUserService.Tests.Kafka;

public sealed class MatchEndedEventMapperTests
{
    private const string MatchId = "99999999-9999-9999-9999-999999999999";
    private const string WhiteId = "11111111-1111-1111-1111-111111111111";
    private const string BlackId = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void NonMatchEndedEvent_MapsToNull()
    {
        MatchEvent ev = new()
        {
            AggregateId = MatchId,
            MoveApplied = new MoveApplied { MoveUci = "e2e4" },
        };

        Assert.Null(MatchEndedEventMapper.Map(ev));
    }

    [Fact]
    public void HumanVsHumanMatchEnded_MapsAllFields()
    {
        MatchEndedFact? fact = MatchEndedEventMapper.Map(Ended(new MatchEnded
        {
            Status = MatchStatus.WhiteWon,
            EndReason = EndReason.Checkmate,
            Source = MatchSource.Native,
            White = new Player { UserId = WhiteId },
            Black = new Player { UserId = BlackId },
        }));

        Assert.NotNull(fact);
        Assert.Equal(MatchId, fact.MatchId);
        Assert.Equal(MatchEndedStatus.WhiteWon, fact.Status);
        Assert.False(fact.External);
        Assert.Equal(new MatchEndedParticipant(WhiteId, null), fact.White);
        Assert.Equal(new MatchEndedParticipant(BlackId, null), fact.Black);
    }

    [Theory]
    [InlineData(MatchStatus.WhiteWon, (int)MatchEndedStatus.WhiteWon)]
    [InlineData(MatchStatus.BlackWon, (int)MatchEndedStatus.BlackWon)]
    [InlineData(MatchStatus.Draw, (int)MatchEndedStatus.Draw)]
    [InlineData(MatchStatus.Ongoing, (int)MatchEndedStatus.Unknown)]
    [InlineData(MatchStatus.Unspecified, (int)MatchEndedStatus.Unknown)]
    public void Status_MapsTerminalValuesAndFallsBackToUnknown(MatchStatus status, int expected)
    {
        MatchEndedFact? fact = MatchEndedEventMapper.Map(Ended(new MatchEnded { Status = status }));

        Assert.Equal((MatchEndedStatus)expected, fact!.Status);
    }

    [Fact]
    public void ExternalSource_MapsToExternal()
    {
        MatchEndedFact? fact = MatchEndedEventMapper.Map(Ended(new MatchEnded
        {
            Status = MatchStatus.Draw,
            Source = MatchSource.External,
        }));

        Assert.True(fact!.External);
    }

    [Fact]
    public void BotSide_MapsEloAndNoUserId()
    {
        MatchEndedFact? fact = MatchEndedEventMapper.Map(Ended(new MatchEnded
        {
            Status = MatchStatus.BlackWon,
            White = new Player { UserId = WhiteId },
            Black = new Player { BotId = "stockfish-3" },
            BlackBotElo = 1500,
        }));

        Assert.Equal(new MatchEndedParticipant(WhiteId, null), fact!.White);
        Assert.Equal(new MatchEndedParticipant(null, 1500), fact.Black);
    }

    [Fact]
    public void WhiteBotSide_MapsEloAndNoUserId()
    {
        MatchEndedFact? fact = MatchEndedEventMapper.Map(Ended(new MatchEnded
        {
            Status = MatchStatus.WhiteWon,
            White = new Player { BotId = "stockfish-9" },
            WhiteBotElo = 2200,
            Black = new Player { UserId = BlackId },
        }));

        Assert.Equal(new MatchEndedParticipant(null, 2200), fact!.White);
        Assert.Equal(new MatchEndedParticipant(BlackId, null), fact.Black);
    }

    [Fact]
    public void BotSideWithoutEloSnapshot_MapsNullElo()
    {
        MatchEndedFact? fact = MatchEndedEventMapper.Map(Ended(new MatchEnded
        {
            Status = MatchStatus.WhiteWon,
            White = new Player { BotId = "stockfish-3" },
            Black = new Player { UserId = BlackId },
        }));

        Assert.Equal(new MatchEndedParticipant(null, null), fact!.White);
    }

    [Fact]
    public void LegacyEventWithoutParticipants_MapsBothSidesAsNonHuman()
    {
        MatchEndedFact? fact = MatchEndedEventMapper.Map(Ended(new MatchEnded
        {
            Status = MatchStatus.WhiteWon,
            EndReason = EndReason.Resignation,
        }));

        Assert.Equal(new MatchEndedParticipant(null, null), fact!.White);
        Assert.Equal(new MatchEndedParticipant(null, null), fact.Black);
    }

    private static MatchEvent Ended(MatchEnded payload) => new()
    {
        EventId = "e-1",
        EventType = "match.MatchEnded",
        AggregateId = MatchId,
        Sequence = 7,
        OccurredAt = 1,
        Producer = "match-manager-service",
        MatchEnded = payload,
    };
}
