using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.GetMetadata;

public static class GetDocumentMetadataEndpoint
{
    public static void MapGetDocumentMetadata(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/{id:guid}", async (
            Guid id,
            GetDocumentMetadataService service,
            CancellationToken ct) =>
        {
            Result<DocumentMetadataResponse> result = await service.HandleAsync(id, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("GetDocumentMetadata")
        .Produces<DocumentMetadataResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization();
    }
}
