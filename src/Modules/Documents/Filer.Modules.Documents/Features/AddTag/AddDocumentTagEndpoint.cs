using Filer.Modules.Documents.Features.ReplaceTags;
using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.AddTag;

public static class AddDocumentTagEndpoint
{
    public static void MapAddDocumentTag(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/{id:guid}/tags/{tagId:guid}", async (
            Guid id,
            Guid tagId,
            AddDocumentTagService service,
            CancellationToken ct) =>
        {
            Result<DocumentTagsResponse> result = await service.HandleAsync(id, tagId, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("AddDocumentTag")
        .RequireAuthorization();
    }
}
