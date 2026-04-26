using System.Security.Claims;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using Microsoft.AspNetCore.Mvc;

namespace MaichessUserService.Rest;

internal static class UsersEndpoints
{
    private const string Collection = "users";

    internal static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/users").RequireAuthorization();

        group.MapGet("/me", GetMe);
        group.MapPatch("/me", PatchMe);

        return routes;
    }

    private static async Task<IResult> GetMe(
        ClaimsPrincipal principal,
        Database.DatabaseClient db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out string userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            GetResponse response = await db.GetAsync(
                new GetRequest { Collection = Collection, Id = userId },
                cancellationToken: ct);
            return Results.Ok(UserResponseFromStruct(response.Record));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> PatchMe(
        [FromBody] PatchUserRequest body,
        ClaimsPrincipal principal,
        Database.DatabaseClient db,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out string userId))
        {
            return Results.Unauthorized();
        }

        if (body.Username is null)
        {
            return Results.UnprocessableEntity(new { error = "at least one field required" });
        }

        if (string.IsNullOrWhiteSpace(body.Username))
        {
            return Results.UnprocessableEntity(new { error = "username must not be empty" });
        }

        Struct fields = new();
        fields.Fields["username"] = Value.ForString(body.Username);

        try
        {
            UpdateResponse response = await db.UpdateAsync(
                new UpdateRequest { Collection = Collection, Id = userId, Fields = fields },
                cancellationToken: ct);
            return Results.Ok(UserResponseFromStruct(response.Record));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Results.NotFound();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            return Results.Conflict(new { error = "username already taken" });
        }
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out string userId)
    {
        string? value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        userId = value ?? string.Empty;
        return !string.IsNullOrEmpty(userId);
    }

    private static UserResponse UserResponseFromStruct(Struct s) =>
        new(
            s.Fields["id"].StringValue,
            s.Fields["username"].StringValue,
            (int)s.Fields["elo"].NumberValue,
            (int)s.Fields["wins"].NumberValue,
            (int)s.Fields["losses"].NumberValue,
            (int)s.Fields["draws"].NumberValue);
}
