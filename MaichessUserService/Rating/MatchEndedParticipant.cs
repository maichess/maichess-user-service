using System.Diagnostics.CodeAnalysis;

namespace MaichessUserService.Rating;

// A side of a finished match: a human (UserId set) or a bot (UserId null; BotElo
// carries the engine-configured strength match-manager snapshotted at creation,
// null for events that predate the snapshot).
[ExcludeFromCodeCoverage]
internal sealed record MatchEndedParticipant(string? UserId, double? BotElo);
