using Filer.SharedKernel.Paging;
using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Search.Features.SearchDocuments;

public static class SearchDocumentsEndpoint
{
    public static void MapSearchDocuments(this IEndpointRouteBuilder routes)
    {
        // Malformed values (?page=abc) fail minimal-API binding and return 400
        // before the handler runs; presence, range and length rules are the
        // slice's own validation (03-api-specification.md).
        routes.MapGet("", async (
            string? q,
            int? page,
            int? pageSize,
            SearchDocumentsService service,
            CancellationToken ct) =>
        {
            var query = new SearchDocumentsQuery(q, page, pageSize);

            Result<PagedResult<SearchHitResponse>> result =
                await service.HandleAsync(query, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("SearchDocuments")
        .Produces<PagedResult<SearchHitResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .RequireAuthorization();
    }
}
