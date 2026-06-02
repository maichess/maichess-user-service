using System.Diagnostics.CodeAnalysis;
using ProtoUser = Maichess.User.V1.User;

namespace MaichessUserService;

[ExcludeFromCodeCoverage]
internal abstract record RecordMatchResultResult
{
    internal sealed record Success(ProtoUser User) : RecordMatchResultResult;

    internal sealed record InvalidInput(string Message) : RecordMatchResultResult;

    internal sealed record NotFound : RecordMatchResultResult;
}
