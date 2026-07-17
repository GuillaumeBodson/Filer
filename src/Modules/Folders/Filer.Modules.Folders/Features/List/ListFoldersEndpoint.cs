using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Folders.Features.List;

public static class ListFoldersEndpoint
{
    public static void MapListFolders(this IEndpointRouteBuilder routes)
    {
        // The view parameter binds as a raw string; whether it names a known view
        // is the slice's own validation, not a binding concern
        // (03-api-specification.md: invalid value → 400).
        routes.MapGet("", async (
            string? view,
            ListFoldersService service,
            CancellationToken ct) =>
        {
            var query = new ListFoldersQuery(view);

            Result<IReadOnlyList<FolderListItemResponse>> result =
                await service.HandleAsync(query, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("ListFolders")
        .Produces<IReadOnlyList<FolderListItemResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .RequireAuthorization();
    }
}
