using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace MaichessUserService.Rest;

[ExcludeFromCodeCoverage]
internal static class UsersEndpoints
{
    internal static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/users").RequireAuthorization();

        group.MapGet("/me", GetMe);
        group.MapPatch("/me", PatchMe);

        return routes;
    }

    private static async Task<IResult> GetMe(
        ClaimsPrincipal principal,
        UsersService usersService,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out string userId))
        {
            return Results.Unauthorized();
        }

        GetUserResult result = await usersService.GetUserAsync(userId, ct);
        return result switch
        {
            GetUserResult.Success ok => Results.Ok(ToResponse(ok.User)),
            GetUserResult.NotFound => Results.NotFound(),
            GetUserResult.InvalidUserId => Results.Unauthorized(),
            _ => Results.Problem(),
        };
    }

    private static async Task<IResult> PatchMe(
        [FromBody] PatchUserRequest body,
        ClaimsPrincipal principal,
        UsersService usersService,
        CancellationToken ct)
    {
        if (!TryGetUserId(principal, out string userId))
        {
            return Results.Unauthorized();
        }

        if (body.Username is null && body.DevMode is null)
        {
            return Results.UnprocessableEntity(new { error = "at least one field required" });
        }

        UpdateUserResult result = await usersService.UpdateUserAsync(userId, body.Username, body.DevMode, ct);
        return result switch
        {
            UpdateUserResult.Success ok => Results.Ok(ToResponse(ok.User)),
            UpdateUserResult.NotFound => Results.NotFound(),
            UpdateUserResult.InvalidInput err => Results.UnprocessableEntity(new { error = err.Message }),
            UpdateUserResult.Conflict => Results.Conflict(new { error = "username already taken" }),
            _ => Results.Problem(),
        };
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out string userId)
    {
        string? value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        userId = value ?? string.Empty;
        return !string.IsNullOrEmpty(userId);
    }

    private static UserResponse ToResponse(Maichess.User.V1.User user) =>
        new(
            Guid.Parse(user.Id),
            user.Username,
            user.Elo,
            user.Wins,
            user.Losses,
            user.Draws,
            user.DevMode,
            user.Rating,
            user.RatingDeviation,
            user.Volatility);
}
