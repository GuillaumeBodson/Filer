using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Tags.Features.Create;

public static class CreateTagEndpoint
{
    public static void MapCreateTag(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("", async (
            CreateTagRequest request,
            CreateTagService service,
            CancellationToken ct) =>
        {
            Result<CreateTagResponse> result = await service.HandleAsync(request, ct);

            return result.IsSuccess
                ? Results.Created($"{TagsRoutes.BasePath}/{result.Value.Id}", result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("CreateTag")
        .Produces<CreateTagResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization();
    }
}
