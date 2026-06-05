using System.Security.Claims;
using Filer.Modules.Auth.Contracts;
using Filer.SharedKernel.Authorization;

namespace Filer.Api.Infrastructure;

/// <summary>
/// Adapts the current request's validated <see cref="ClaimsPrincipal"/> to
/// <see cref="ICurrentUser"/> so feature services and the
/// <see cref="OwnershipGuard"/> read the caller's identity from the token, never
/// from client-supplied input (05-security.md). The owner id is the subject claim
/// the Auth module writes (<see cref="AuthClaimTypes.Subject"/>); the tenant id is
/// reserved for the future multi-tenant evolution (02-data-model.md).
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true && TryGetId(out _);

    public Guid Id => TryGetId(out Guid id)
        ? id
        : throw new InvalidOperationException("The current request has no authenticated user.");

    public Guid? TenantId =>
        Guid.TryParse(Principal?.FindFirstValue(AuthClaimTypes.TenantId), out Guid tenantId)
            ? tenantId
            : null;

    private bool TryGetId(out Guid id) =>
        Guid.TryParse(Principal?.FindFirstValue(AuthClaimTypes.Subject), out id);
}
