using Grpc.Core;

namespace MaichessUserService.Tests.Support;

internal static class GrpcHelper
{
    internal static AsyncUnaryCall<T> GrpcCall<T>(T response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });

    internal static AsyncUnaryCall<T> GrpcCallFailed<T>(StatusCode code, string detail = "") =>
        new(
            Task.FromException<T>(new RpcException(new Status(code, detail))),
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });
}
