using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using Maichess.User.V1;
using ProtoUser = Maichess.User.V1.User;

namespace MaichessUserService.Grpc;

internal sealed class UsersGrpcService(Database.DatabaseClient db) : Users.UsersBase
{
    private const string Collection = "users";

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

        Struct record = new();
        record.Fields["username"] = Value.ForString(request.Username);
        record.Fields["password_hash"] = Value.ForString(request.PasswordHash);
        record.Fields["elo"] = Value.ForNumber(1200);
        record.Fields["wins"] = Value.ForNumber(0);
        record.Fields["losses"] = Value.ForNumber(0);
        record.Fields["draws"] = Value.ForNumber(0);

        try
        {
            InsertResponse response = await db.InsertAsync(
                new InsertRequest { Collection = Collection, Record = record },
                cancellationToken: context.CancellationToken);
            return new CreateUserResponse { User = UserFromStruct(response.Record) };
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "username already taken"));
        }
    }

    public override async Task<GetUserResponse> GetUser(
        GetUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out _))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id must be a valid UUID"));
        }

        try
        {
            GetResponse response = await db.GetAsync(
                new GetRequest { Collection = Collection, Id = request.UserId },
                cancellationToken: context.CancellationToken);
            return new GetUserResponse { User = UserFromStruct(response.Record) };
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"user {request.UserId} not found"));
        }
    }

    public override async Task<UpdateUserResponse> UpdateUser(
        UpdateUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out _))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id must be a valid UUID"));
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "username is required"));
        }

        Struct fields = new();
        fields.Fields["username"] = Value.ForString(request.Username);

        try
        {
            UpdateResponse response = await db.UpdateAsync(
                new UpdateRequest { Collection = Collection, Id = request.UserId, Fields = fields },
                cancellationToken: context.CancellationToken);
            return new UpdateUserResponse { User = UserFromStruct(response.Record) };
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"user {request.UserId} not found"));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "username already taken"));
        }
    }

    private static ProtoUser UserFromStruct(Struct s) => new()
    {
        Id = s.Fields["id"].StringValue,
        Username = s.Fields["username"].StringValue,
        Elo = (int)s.Fields["elo"].NumberValue,
        Wins = (int)s.Fields["wins"].NumberValue,
        Losses = (int)s.Fields["losses"].NumberValue,
        Draws = (int)s.Fields["draws"].NumberValue,
    };
}
