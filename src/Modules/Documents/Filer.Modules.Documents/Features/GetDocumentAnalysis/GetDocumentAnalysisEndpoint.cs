using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.GetDocumentAnalysis;

public static class GetDocumentAnalysisEndpoint
{
    public static void MapGetDocumentAnalysis(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/{id:guid}/analysis", async (
            Guid id,
            GetDocumentAnalysisService service,
            CancellationToken ct) =>
        {
            Result<DocumentAnalysisResponse> result = await service.HandleAsync(id, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("GetDocumentAnalysis")
        .RequireAuthorization();
    }
}
