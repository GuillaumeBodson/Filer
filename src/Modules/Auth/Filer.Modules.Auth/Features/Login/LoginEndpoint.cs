using Filer.Modules.Auth.Web;
using Filer.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Auth.Features.Login;

public static class LoginEndpoint
{
    public static void MapLogin(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/login", async (
            LoginRequest request,
            LoginService service,
            CancellationToken ct) =>
        {
            Result<LoginResponse> result = await service.HandleAsync(request, ct);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error!.ToHttpResult();
        })
        .WithName("Login")
        .AllowAnonymous();
    }
}
