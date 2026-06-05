using Filer.SharedKernel.Domain;
using Filer.SharedKernel.Results;

namespace Filer.SharedKernel.Authorization;

/// <summary>
/// The single ownership-enforcement primitive reused by every protected slice
/// (05-security.md). Given the authenticated caller and a resource that may or may
/// not exist, it returns the resource only when the caller owns it. A missing
/// resource, an unauthenticated caller, and a resource owned by someone else all
/// collapse to the same <see cref="Error.NotFound(string, string)"/> so the API
/// never reveals that a resource the caller does not own exists — cross-owner
/// access is 404, not 403 (03-api-specification.md, 05-security.md).
/// </summary>
public static class OwnershipGuard
{
    /// <summary>
    /// Returns <paramref name="resource"/> when the authenticated
    /// <paramref name="currentUser"/> owns it; otherwise a not-found failure.
    /// Null (does not exist), an unauthenticated caller, and a different owner are
    /// deliberately indistinguishable to the client — that indistinguishability is
    /// the security property, not an accident of implementation.
    /// </summary>
    public static Result<T> Require<T>(ICurrentUser currentUser, T? resource)
        where T : class, IOwnedEntity
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (resource is null || !currentUser.IsAuthenticated || resource.OwnerId != currentUser.Id)
        {
            return Result.Failure<T>(Error.NotFound());
        }

        return Result.Success(resource);
    }
}
