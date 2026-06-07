using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Folders.Features.Delete;

public static class DeleteFolderEndpoint
{
    public static void MapDeleteFolder(this IEndpointRouteBuilder routes)
    {
        // A malformed ?recursive= value fails minimal-API binding and returns 400
        // before the handler runs; absence means the safe default — reject
        // non-empty (ADR-007).
        routes.MapDelete("/{id:guid}", async (
            Guid id,
            bool? recursive,
            DeleteFolderService service,
            CancellationToken ct) =>
        {
            Result result = await service.HandleAsync(id, recursive ?? false, ct);

            return result.IsSuccess
                ? Results.NoContent()
                : result.Error!.ToHttpResult();
        })
        .WithName("DeleteFolder")
        .RequireAuthorization();
    }
}
