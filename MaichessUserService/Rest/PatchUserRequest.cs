using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessUserService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record PatchUserRequest(
    string? Username,
    [property: JsonPropertyName("dev_mode")] bool? DevMode);
