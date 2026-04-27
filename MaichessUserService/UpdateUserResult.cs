using System.Diagnostics.CodeAnalysis;
using ProtoUser = Maichess.User.V1.User;

namespace MaichessUserService;

[ExcludeFromCodeCoverage]
internal abstract record UpdateUserResult
{
    internal sealed record Success(ProtoUser User) : UpdateUserResult;

    internal sealed record InvalidInput(string Message) : UpdateUserResult;

    internal sealed record NotFound : UpdateUserResult;

    internal sealed record Conflict : UpdateUserResult;
}
