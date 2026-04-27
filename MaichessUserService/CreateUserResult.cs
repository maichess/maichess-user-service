using System.Diagnostics.CodeAnalysis;
using ProtoUser = Maichess.User.V1.User;

namespace MaichessUserService;

[ExcludeFromCodeCoverage]
internal abstract record CreateUserResult
{
    internal sealed record Success(ProtoUser User) : CreateUserResult;

    internal sealed record InvalidInput(string Message) : CreateUserResult;

    internal sealed record Conflict : CreateUserResult;
}
