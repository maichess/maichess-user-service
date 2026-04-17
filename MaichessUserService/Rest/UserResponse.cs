namespace MaichessUserService.Rest;

internal sealed record UserResponse(
    Guid Id,
    string Username,
    int Elo,
    int Wins,
    int Losses,
    int Draws);
