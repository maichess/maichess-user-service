using Maichess.Events.V1;
using MaichessUserService.Rating;

namespace MaichessUserService.Kafka;

// Pure transform: a match.events.v1 envelope -> the MatchEndedFact the rating
// trigger consumes, or null for anything that is not a MatchEnded. Events from
// before the kafka-08 enrichment carry no participants; both sides then map to
// non-humans and the trigger records nothing, which is correct — those matches
// predate the event-driven rating path.
internal static class MatchEndedEventMapper
{
    internal static MatchEndedFact? Map(MatchEvent ev)
    {
        if (ev.PayloadCase != MatchEvent.PayloadOneofCase.MatchEnded)
        {
            return null;
        }

        MatchEnded ended = ev.MatchEnded;
        return new MatchEndedFact(
            ev.AggregateId,
            StatusOf(ended.Status),
            ended.Source == MatchSource.External,
            ParticipantOf(ended.White, ended.HasWhiteBotElo ? ended.WhiteBotElo : null),
            ParticipantOf(ended.Black, ended.HasBlackBotElo ? ended.BlackBotElo : null));
    }

    private static MatchEndedStatus StatusOf(MatchStatus status) => status switch
    {
        MatchStatus.WhiteWon => MatchEndedStatus.WhiteWon,
        MatchStatus.BlackWon => MatchEndedStatus.BlackWon,
        MatchStatus.Draw => MatchEndedStatus.Draw,
        _ => MatchEndedStatus.Unknown,
    };

    private static MatchEndedParticipant ParticipantOf(Player? player, double? botElo) =>
        new(
            player?.IdentityCase == Player.IdentityOneofCase.UserId ? player.UserId : null,
            botElo);
}
