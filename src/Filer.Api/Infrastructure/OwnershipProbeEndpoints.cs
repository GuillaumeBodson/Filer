using System.Collections.Concurrent;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Domain;
using Filer.SharedKernel.Results;
using Filer.WebKernel;

namespace Filer.Api.Infrastructure;

/// <summary>
/// A deliberately throwaway owned-resource slice, mapped ONLY under the Testing
/// environment. It exists so the cross-owner → 404 guarantee (05-security.md) — the
/// single most important behavioural guarantee in the system — can be exercised end
/// to end through the real JWT → <see cref="ICurrentUser"/> → <see cref="OwnershipGuard"/>
/// → problem-details chain before the first real owned-resource module (Documents,
/// 08 build order) lands. Once Documents exists, the live ownership integration test
/// covers this against a real resource and this scaffold can be deleted.
/// </summary>
internal static class OwnershipProbeEndpoints
{
    /// <summary>Collection route for the probe resource (versioned like every slice).</summary>
    public const string BasePath = ApiRoutes.V1 + "/_probe/resources";

    // In-memory only: the probe never persists. Static so the resource created by
    // one request is visible to the next within the same test host.
    private static readonly ConcurrentDictionary<Guid, ProbeResource> Resources = new();

    public static IEndpointRouteBuilder MapOwnershipProbeEndpoints(this IEndpointRouteBuilder routes)
    {
        // Creates a resource owned by the authenticated caller.
        routes.MapPost(BasePath, (ICurrentUser currentUser) =>
        {
            var resource = new ProbeResource(Guid.NewGuid(), currentUser.Id);
            Resources[resource.Id] = resource;
            return Results.Ok(new ProbeResourceResponse(resource.Id));
        })
        .RequireAuthorization();

        // Reads a resource through the ownership guard: 200 if owned, 404 otherwise
        // — a not-owned resource and a non-existent one are indistinguishable.
        routes.MapGet($"{BasePath}/{{id:guid}}", (Guid id, ICurrentUser currentUser) =>
        {
            Resources.TryGetValue(id, out ProbeResource? resource);

            Result<ProbeResource> result = OwnershipGuard.Require(currentUser, resource);

            return result.IsSuccess
                ? Results.Ok(new ProbeResourceResponse(result.Value.Id))
                : result.Error!.ToHttpResult();
        })
        .RequireAuthorization();

        return routes;
    }

    private sealed record ProbeResource(Guid Id, Guid OwnerId, Guid? TenantId = null) : IOwnedEntity;

    private sealed record ProbeResourceResponse(Guid Id);
}
