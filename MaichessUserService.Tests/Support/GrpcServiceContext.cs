using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using NSubstitute;

namespace MaichessUserService.Tests.Support;

internal sealed class GrpcServiceContext
{
    private readonly Database.DatabaseClient _db = Substitute.For<Database.DatabaseClient>();
    private readonly Database.DatabaseClient _conflictingDb = Substitute.For<Database.DatabaseClient>();

    internal UsersService Service { get; }

    internal UsersService ConflictService { get; }

    internal bool UseConflictingService { get; set; }

    internal CreateUserResult? CreateUserResult { get; set; }

    internal GetUserResult? GetUserResult { get; set; }

    internal UpdateUserResult? UpdateUserResult { get; set; }

    internal InsertRequest? LastInsertRequest { get; set; }

    internal UsersService ActiveService => UseConflictingService ? ConflictService : Service;

    internal GrpcServiceContext()
    {
        _db.InsertAsync(
            Arg.Any<InsertRequest>(),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                InsertRequest request = callInfo.Arg<InsertRequest>();
                LastInsertRequest = request;
                Struct record = request.Record.Clone();
                record.Fields["id"] = Value.ForString(Guid.NewGuid().ToString());
                return GrpcHelper.GrpcCall(new InsertResponse { Record = record });
            });

        _db.GetAsync(
            Arg.Any<GetRequest>(),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<GetResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.NotFound, string.Empty)));

        _db.UpdateAsync(
            Arg.Any<UpdateRequest>(),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<UpdateResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.NotFound, string.Empty)));

        _conflictingDb.InsertAsync(
            Arg.Any<InsertRequest>(),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<InsertResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.AlreadyExists, string.Empty)));

        _conflictingDb.GetAsync(
            Arg.Any<GetRequest>(),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<GetResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.NotFound, string.Empty)));

        _conflictingDb.UpdateAsync(
            Arg.Any<UpdateRequest>(),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<UpdateResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.AlreadyExists, string.Empty)));

        Service = new UsersService(_db);
        ConflictService = new UsersService(_conflictingDb);
    }

    internal void SeedUser(string id, string username, bool? devMode = false)
    {
        Struct record = BuildRecord(id, username, devMode);

        _db.GetAsync(
            Arg.Is<GetRequest>(r => r.Id == id),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(new GetResponse { Record = record }));

        _db.UpdateAsync(
            Arg.Is<UpdateRequest>(r => r.Id == id),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Struct updated = record.Clone();
                foreach (var field in callInfo.Arg<UpdateRequest>().Fields.Fields)
                {
                    updated.Fields[field.Key] = field.Value;
                }

                return GrpcHelper.GrpcCall(new UpdateResponse { Record = updated });
            });

        _conflictingDb.GetAsync(
            Arg.Is<GetRequest>(r => r.Id == id),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(new GetResponse { Record = record }));
    }

    private static Struct BuildRecord(string id, string username, bool? devMode)
    {
        Struct record = new()
        {
            Fields =
            {
                ["id"] = Value.ForString(id),
                ["username"] = Value.ForString(username),
                ["password_hash"] = Value.ForString(string.Empty),
                ["elo"] = Value.ForNumber(1200),
                ["wins"] = Value.ForNumber(0),
                ["losses"] = Value.ForNumber(0),
                ["draws"] = Value.ForNumber(0),
            },
        };

        // A null devMode models a legacy record stored before the field existed.
        if (devMode is not null)
        {
            record.Fields["dev_mode"] = Value.ForBool(devMode.Value);
        }

        return record;
    }
}
