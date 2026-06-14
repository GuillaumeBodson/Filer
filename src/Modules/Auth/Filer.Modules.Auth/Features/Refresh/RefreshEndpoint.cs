using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Auth.Features.Refresh;

public static class RefreshEndpoint
{
    public static void MapRefresh(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/refresh", async (
            RefreshRequest request,
            RefreshService service,
            CancellationToken ct) =>
        {
            Result<RefreshResponse> result = await service.HandleAsync(request, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("Refresh")
        .Produces<RefreshResponse>(StatusCodes.Status200OK)
        .AllowAnonymous();
    }
}
