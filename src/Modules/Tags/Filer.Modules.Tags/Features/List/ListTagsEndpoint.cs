using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Tags.Features.List;

public static class ListTagsEndpoint
{
    public static void MapListTags(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("", async (
            ListTagsService service,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<TagListItemResponse>> result = await service.HandleAsync(ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("ListTags")
        .RequireAuthorization();
    }
}
