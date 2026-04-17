using System.Security.Claims;
using MaichessUserService.Data;
using MaichessUserService.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MaichessUserService.Rest;

internal static class UsersEndpoints
{
    internal static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/users").RequireAuthorization();

        group.MapGet("/{id:guid}", GetUser);
        group.MapPatch("/{id:guid}", PatchUser);

        return routes;
    }

    private static async Task<IResult> GetUser(
        Guid id,
        UserDbContext db,
        CancellationToken ct)
    {
        User? user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        return user is null
            ? Results.NotFound()
            : Results.Ok(MapToResponse(user));
    }

    private static async Task<IResult> PatchUser(
        Guid id,
        [FromBody] PatchUserRequest body,
        ClaimsPrincipal principal,
        UserDbContext db,
        CancellationToken ct)
    {
        string? tokenUserId = principal.FindFirstValue("user_id");

        if (tokenUserId is null || !string.Equals(tokenUserId, id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }

        if (body.Username is null)
        {
            return Results.UnprocessableEntity(new { error = "at least one field required" });
        }

        if (string.IsNullOrWhiteSpace(body.Username))
        {
            return Results.UnprocessableEntity(new { error = "username must not be empty" });
        }

        User? user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return Results.NotFound();
        }

        user.Username = body.Username;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                  && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new { error = "username already taken" });
        }

        return Results.Ok(MapToResponse(user));
    }

    private static UserResponse MapToResponse(User user) =>
        new(user.Id, user.Username, user.Elo, user.Wins, user.Losses, user.Draws);
}
