using System.Security.Claims;
using Filer.Modules.Auth.Contracts;
using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Auth.Features.Logout;

/// <summary>
/// Revokes the authenticated caller's refresh token (and its rotation family).
/// Requires authorization; the caller's identity comes from the validated token's
/// claims, never from the body, so one user can never log another out
/// (05-security.md). Returns 204 on success — revocation has no representation.
/// </summary>
public static class LogoutEndpoint
{
    public static void MapLogout(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/logout", async (
            LogoutRequest request,
            ClaimsPrincipal principal,
            LogoutService service,
            CancellationToken ct) =>
        {
            string? subject = principal.FindFirstValue(AuthClaimTypes.Subject);
            if (!Guid.TryParse(subject, out Guid userId))
            {
                return Results.Unauthorized();
            }

            Result result = await service.HandleAsync(userId, request, ct);

            return result.IsSuccess
                ? Results.NoContent()
                : result.Error!.ToHttpResult();
        })
        .WithName("Logout")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .RequireAuthorization();
    }
}
