using Filer.Modules.Documents.Features.ReplaceTags;
using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.GetTags;

public static class GetDocumentTagsEndpoint
{
    public static void MapGetDocumentTags(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/{id:guid}/tags", async (
            Guid id,
            GetDocumentTagsService service,
            CancellationToken ct) =>
        {
            Result<DocumentTagsResponse> result = await service.HandleAsync(id, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("GetDocumentTags")
        .Produces<DocumentTagsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization();
    }
}
