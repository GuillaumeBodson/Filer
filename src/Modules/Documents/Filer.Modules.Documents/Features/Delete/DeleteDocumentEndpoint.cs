using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.Delete;

public static class DeleteDocumentEndpoint
{
    public static void MapDeleteDocument(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/{id:guid}", async (
            Guid id,
            DeleteDocumentService service,
            CancellationToken ct) =>
        {
            Result result = await service.HandleAsync(id, ct);

            return result.IsSuccess
                ? Results.NoContent()
                : result.Error!.ToHttpResult();
        })
        .WithName("DeleteDocument")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization();
    }
}
