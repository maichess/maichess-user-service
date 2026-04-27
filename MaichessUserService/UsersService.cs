using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using ProtoUser = Maichess.User.V1.User;

namespace MaichessUserService;

internal sealed class UsersService(Database.DatabaseClient db)
{
    private const string Collection = "users";

    internal async Task<CreateUserResult> CreateUserAsync(string username, string passwordHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new CreateUserResult.InvalidInput("username is required");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return new CreateUserResult.InvalidInput("password_hash is required");
        }

        Struct record = new();
        record.Fields["username"] = Value.ForString(username);
        record.Fields["password_hash"] = Value.ForString(passwordHash);
        record.Fields["elo"] = Value.ForNumber(1200);
        record.Fields["wins"] = Value.ForNumber(0);
        record.Fields["losses"] = Value.ForNumber(0);
        record.Fields["draws"] = Value.ForNumber(0);

        try
        {
            InsertResponse response = await db.InsertAsync(
                new InsertRequest { Collection = Collection, Record = record },
                cancellationToken: ct);
            return new CreateUserResult.Success(UserFromStruct(response.Record));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            return new CreateUserResult.Conflict();
        }
    }

    internal async Task<GetUserResult> GetUserAsync(string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out _))
        {
            return new GetUserResult.InvalidUserId();
        }

        try
        {
            GetResponse response = await db.GetAsync(
                new GetRequest { Collection = Collection, Id = userId },
                cancellationToken: ct);
            return new GetUserResult.Success(UserFromStruct(response.Record));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return new GetUserResult.NotFound();
        }
    }

    internal async Task<UpdateUserResult> UpdateUserAsync(string userId, string username, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out _))
        {
            return new UpdateUserResult.InvalidInput("user_id must be a valid UUID");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return new UpdateUserResult.InvalidInput("username is required");
        }

        Struct fields = new();
        fields.Fields["username"] = Value.ForString(username);

        try
        {
            UpdateResponse response = await db.UpdateAsync(
                new UpdateRequest { Collection = Collection, Id = userId, Fields = fields },
                cancellationToken: ct);
            return new UpdateUserResult.Success(UserFromStruct(response.Record));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return new UpdateUserResult.NotFound();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            return new UpdateUserResult.Conflict();
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
