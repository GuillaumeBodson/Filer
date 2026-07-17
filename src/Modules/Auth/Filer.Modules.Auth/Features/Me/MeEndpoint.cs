using System.Security.Claims;
using Filer.Modules.Auth.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Auth.Features.Me;

/// <summary>
/// Returns the authenticated user's profile, read from the validated token's
/// claims. Requires authorization (05-security.md).
/// </summary>
public static class MeEndpoint
{
    public static void MapMe(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            string? subject = principal.FindFirstValue(AuthClaimTypes.Subject);
            string? email = principal.FindFirstValue(AuthClaimTypes.Email);

            if (!Guid.TryParse(subject, out Guid id))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new MeResponse(id, email ?? string.Empty));
        })
        .WithName("Me")
        .Produces<MeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .RequireAuthorization();
    }
}
