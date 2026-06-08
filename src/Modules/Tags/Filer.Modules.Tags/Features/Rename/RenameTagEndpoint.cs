using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Tags.Features.Rename;

public static class RenameTagEndpoint
{
    public static void MapRenameTag(this IEndpointRouteBuilder routes)
    {
        routes.MapPatch("/{id:guid}", async (
            Guid id,
            RenameTagRequest request,
            RenameTagService service,
            CancellationToken ct) =>
        {
            Result<RenameTagResponse> result = await service.HandleAsync(id, request, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("RenameTag")
        .RequireAuthorization();
    }
}
