using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.ApplyDocumentAnalysis;

public static class ApplyDocumentAnalysisEndpoint
{
    public static void MapApplyDocumentAnalysis(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/{id:guid}/analysis/apply", async (
            Guid id,
            ApplyDocumentAnalysisRequest request,
            ApplyDocumentAnalysisService service,
            CancellationToken ct) =>
        {
            Result<ApplyDocumentAnalysisResponse> result = await service.HandleAsync(id, request, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("ApplyDocumentAnalysis")
        .RequireAuthorization();
    }
}
