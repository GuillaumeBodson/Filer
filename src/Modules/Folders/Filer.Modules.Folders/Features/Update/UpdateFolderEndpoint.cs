using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Folders.Features.Update;

public static class UpdateFolderEndpoint
{
    public static void MapUpdateFolder(this IEndpointRouteBuilder routes)
    {
        routes.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateFolderRequest request,
            UpdateFolderService service,
            CancellationToken ct) =>
        {
            Result<UpdateFolderResponse> result = await service.HandleAsync(id, request, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("UpdateFolder")
        .Produces<UpdateFolderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization();
    }
}
