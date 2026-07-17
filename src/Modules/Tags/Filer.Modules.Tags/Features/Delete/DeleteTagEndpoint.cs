using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Tags.Features.Delete;

public static class DeleteTagEndpoint
{
    public static void MapDeleteTag(this IEndpointRouteBuilder routes)
    {
        routes.MapDelete("/{id:guid}", async (
            Guid id,
            DeleteTagService service,
            CancellationToken ct) =>
        {
            Result result = await service.HandleAsync(id, ct);

            return result.IsSuccess
                ? Results.NoContent()
                : result.Error!.ToHttpResult();
        })
        .WithName("DeleteTag")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization();
    }
}
