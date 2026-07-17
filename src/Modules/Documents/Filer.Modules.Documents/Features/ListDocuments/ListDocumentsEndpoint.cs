using Filer.SharedKernel.Paging;
using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.ListDocuments;

public static class ListDocumentsEndpoint
{
    public static void MapListDocuments(this IEndpointRouteBuilder routes)
    {
        // Malformed values (?page=abc, ?folderId=not-a-guid) fail minimal-API
        // binding and return 400 before the handler runs; range and length rules
        // are the slice's own validation (03-api-specification.md).
        routes.MapGet("", async (
            Guid? folderId,
            Guid? tagId,
            string? q,
            int? page,
            int? pageSize,
            ListDocumentsService service,
            CancellationToken ct) =>
        {
            var query = new ListDocumentsQuery(folderId, tagId, q, page, pageSize);

            Result<PagedResult<DocumentListItemResponse>> result =
                await service.HandleAsync(query, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("ListDocuments")
        .Produces<PagedResult<DocumentListItemResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .RequireAuthorization();
    }
}
