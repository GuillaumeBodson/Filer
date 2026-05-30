namespace Filer.SharedKernel.Authorization;

/// <summary>
/// Provides the authenticated user's identity to feature services so ownership
/// checks never trust an id supplied by the client (05-security.md). Implemented
/// by the host over the current <c>ClaimsPrincipal</c>.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid Id { get; }

    Guid? TenantId { get; }
}
