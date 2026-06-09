using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.RemoveTag;

public static class RemoveDocumentTagEndpoint
{
    public static void MapRemoveDocumentTag(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/{id:guid}/tags/{tagId:guid}", async (
            Guid id,
            Guid tagId,
            RemoveDocumentTagService service,
            CancellationToken ct) =>
        {
            Result result = await service.HandleAsync(id, tagId, ct);

            return result.IsSuccess
                ? Results.NoContent()
                : result.Error!.ToHttpResult();
        })
        .WithName("RemoveDocumentTag")
        .RequireAuthorization();
    }
}
