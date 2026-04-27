using System.Diagnostics.CodeAnalysis;

namespace MaichessUserService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record PatchUserRequest(string? Username);
