using System.Diagnostics.CodeAnalysis;

namespace MaichessUserService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record UserResponse(
    Guid Id,
    string Username,
    int Elo,
    int Wins,
    int Losses,
    int Draws);
