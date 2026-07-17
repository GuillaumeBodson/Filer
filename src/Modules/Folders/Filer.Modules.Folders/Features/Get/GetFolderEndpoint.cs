using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Folders.Features.Get;

public static class GetFolderEndpoint
{
    public static void MapGetFolder(this IEndpointRouteBuilder routes)
    {
        // A non-guid id fails the route constraint and is a routing 404 before
        // the handler runs — indistinguishable from a missing folder, which is
        // exactly the uniform-404 stance (05-security.md).
        routes.MapGet("/{id:guid}", async (
            Guid id,
            GetFolderService service,
            CancellationToken ct) =>
        {
            Result<GetFolderResponse> result = await service.HandleAsync(id, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("GetFolder")
        .Produces<GetFolderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization();
    }
}
