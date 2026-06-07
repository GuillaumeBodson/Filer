using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.UpdateMetadata;

public static class UpdateDocumentMetadataEndpoint
{
    public static void MapUpdateDocumentMetadata(this IEndpointRouteBuilder routes)
    {
        routes.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateDocumentMetadataRequest request,
            UpdateDocumentMetadataService service,
            CancellationToken ct) =>
        {
            Result<UpdateDocumentMetadataResponse> result =
                await service.HandleAsync(id, request, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("UpdateDocumentMetadata")
        .RequireAuthorization();
    }
}
