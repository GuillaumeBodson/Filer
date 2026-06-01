using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Auth.Features.Register;

/// <summary>
/// Minimal API endpoint: binds the request, calls the service, maps the result to
/// a typed HTTP response (10-solution-structure.md).
/// </summary>
public static class RegisterEndpoint
{
    public static void MapRegister(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/register", async (
            RegisterRequest request,
            RegisterService service,
            CancellationToken ct) =>
        {
            Result<RegisterResponse> result = await service.HandleAsync(request, ct);

            return result.IsSuccess
                ? Results.Created($"{AuthRoutes.BasePath}/users/{result.Value.Id}", result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("Register")
        .AllowAnonymous();
    }
}
