using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.DownloadContent;

public static class DownloadDocumentContentEndpoint
{
    public static void MapDownloadDocumentContent(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/{id:guid}/content", async (
            Guid id,
            DownloadDocumentContentService service,
            CancellationToken ct) =>
        {
            Result<DownloadDocumentContentResult> result = await service.HandleAsync(id, ct);
            if (result.IsFailure)
            {
                return result.Error!.ToHttpResult();
            }

            DownloadDocumentContentResult content = result.Value;

            // Results.File disposes the stream after the response is written and
            // emits Content-Disposition with the RFC 6266-encoded original name —
            // the framework, not us, handles hostile file names (05-security.md).
            return Results.File(
                content.Content,
                contentType: content.ContentType,
                fileDownloadName: content.FileName);
        })
        .WithName("DownloadDocumentContent")
        .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization();
    }
}
