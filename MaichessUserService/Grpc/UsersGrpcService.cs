using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Maichess.User.V1;

namespace MaichessUserService.Grpc;

[ExcludeFromCodeCoverage]
internal sealed class UsersGrpcService(UsersService usersService) : Users.UsersBase
{
    public override async Task<CreateUserResponse> CreateUser(CreateUserRequest request, ServerCallContext context)
    {
        CreateUserResult result = await usersService.CreateUserAsync(
            request.Username, request.PasswordHash, context.CancellationToken);
        return result switch
        {
            CreateUserResult.Success ok => new CreateUserResponse { User = ok.User },
            CreateUserResult.InvalidInput err => throw new RpcException(new Status(StatusCode.InvalidArgument, err.Message)),
            CreateUserResult.Conflict => throw new RpcException(new Status(StatusCode.AlreadyExists, "username already taken")),
            _ => throw new RpcException(new Status(StatusCode.Internal, "unexpected result")),
        };
    }

    public override async Task<GetUserResponse> GetUser(GetUserRequest request, ServerCallContext context)
    {
        GetUserResult result = await usersService.GetUserAsync(request.UserId, context.CancellationToken);
        return result switch
        {
            GetUserResult.Success ok => new GetUserResponse { User = ok.User },
            GetUserResult.InvalidUserId => throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id must be a valid UUID")),
            GetUserResult.NotFound => throw new RpcException(new Status(StatusCode.NotFound, $"user {request.UserId} not found")),
            _ => throw new RpcException(new Status(StatusCode.Internal, "unexpected result")),
        };
    }

    public override async Task<UpdateUserResponse> UpdateUser(UpdateUserRequest request, ServerCallContext context)
    {
        string? username = string.IsNullOrEmpty(request.Username) ? null : request.Username;
        bool? devMode = request.HasDevMode ? request.DevMode : null;
        UpdateUserResult result = await usersService.UpdateUserAsync(
            request.UserId, username, devMode, context.CancellationToken);
        return result switch
        {
            UpdateUserResult.Success ok => new UpdateUserResponse { User = ok.User },
            UpdateUserResult.InvalidInput err => throw new RpcException(new Status(StatusCode.InvalidArgument, err.Message)),
            UpdateUserResult.NotFound => throw new RpcException(new Status(StatusCode.NotFound, $"user {request.UserId} not found")),
            UpdateUserResult.Conflict => throw new RpcException(new Status(StatusCode.AlreadyExists, "username already taken")),
            _ => throw new RpcException(new Status(StatusCode.Internal, "unexpected result")),
        };
    }
}
