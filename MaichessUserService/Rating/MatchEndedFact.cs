using System.Diagnostics.CodeAnalysis;

namespace MaichessUserService.Rating;

// The end-of-match fact the Kafka MatchEnded consumer hands to the rating side,
// decoupled from the proto event so the trigger + idempotency logic is testable
// without serde concerns.
[ExcludeFromCodeCoverage]
internal sealed record MatchEndedFact(
    string MatchId,
    MatchEndedStatus Status,
    bool External,
    MatchEndedParticipant White,
    MatchEndedParticipant Black);
