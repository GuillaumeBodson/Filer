using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Folders.Features.Create;

public static class CreateFolderEndpoint
{
    public static void MapCreateFolder(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("", async (
            CreateFolderRequest request,
            CreateFolderService service,
            CancellationToken ct) =>
        {
            Result<CreateFolderResponse> result = await service.HandleAsync(request, ct);

            return result.IsSuccess
                ? Results.Created($"{FoldersRoutes.BasePath}/{result.Value.Id}", result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("CreateFolder")
        .RequireAuthorization();
    }
}
