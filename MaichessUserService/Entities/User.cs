namespace MaichessUserService.Entities;

internal sealed class User
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public int Elo { get; set; } = 1200;

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Draws { get; set; }
}
