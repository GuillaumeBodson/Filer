using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.ReplaceTags;

public static class ReplaceDocumentTagsEndpoint
{
    public static void MapReplaceDocumentTags(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/{id:guid}/tags", async (
            Guid id,
            ReplaceDocumentTagsRequest request,
            ReplaceDocumentTagsService service,
            CancellationToken ct) =>
        {
            Result<DocumentTagsResponse> result = await service.HandleAsync(id, request, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("ReplaceDocumentTags")
        .Produces<DocumentTagsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization();
    }
}
