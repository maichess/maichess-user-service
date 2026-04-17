using Grpc.Core;
using Maichess.User.V1;
using MaichessUserService.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProtoUser = Maichess.User.V1.User;

namespace MaichessUserService.Grpc;

internal sealed class UsersGrpcService(UserDbContext db) : Users.UsersBase
{
    public override async Task<CreateUserResponse> CreateUser(
        CreateUserRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "username is required"));
        }

        if (string.IsNullOrWhiteSpace(request.PasswordHash))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "password_hash is required"));
        }

        var user = new Entities.User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            PasswordHash = request.PasswordHash,
        };

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                  && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "username already taken"));
        }

        return new CreateUserResponse { User = MapToProto(user) };
    }

    public override async Task<GetUserResponse> GetUser(
        GetUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out Guid userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id must be a valid UUID"));
        }

        Entities.User user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"user {request.UserId} not found"));

        return new GetUserResponse { User = MapToProto(user) };
    }

    public override async Task<UpdateUserResponse> UpdateUser(
        UpdateUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out Guid userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id must be a valid UUID"));
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "username is required"));
        }

        Entities.User user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"user {request.UserId} not found"));

        user.Username = request.Username;

        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                  && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "username already taken"));
        }

        return new UpdateUserResponse { User = MapToProto(user) };
    }

    private static ProtoUser MapToProto(Entities.User user) =>
        new()
        {
            Id = user.Id.ToString(),
            Username = user.Username,
            Elo = user.Elo,
            Wins = user.Wins,
            Losses = user.Losses,
            Draws = user.Draws,
        };
}
