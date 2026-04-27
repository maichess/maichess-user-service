using System.Diagnostics.CodeAnalysis;
using ProtoUser = Maichess.User.V1.User;

namespace MaichessUserService;

[ExcludeFromCodeCoverage]
internal abstract record GetUserResult
{
    internal sealed record Success(ProtoUser User) : GetUserResult;

    internal sealed record InvalidUserId : GetUserResult;

    internal sealed record NotFound : GetUserResult;
}
