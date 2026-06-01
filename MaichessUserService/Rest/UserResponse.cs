using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessUserService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record UserResponse(
    Guid Id,
    string Username,
    int Elo,
    int Wins,
    int Losses,
    int Draws,
    [property: JsonPropertyName("dev_mode")] bool DevMode);
