using Xunit;

namespace Filer.IntegrationTests.Ownership;

/// <summary>
/// Cross-owner access to a protected resource must return 404, not 403 or 200
/// (05-security.md) — the single most important behavioural guarantee in the
/// system, and one that must never regress (12-testing-strategy.md).
///
/// It cannot be exercised yet: no owned resource exists (only Auth: register /
/// login / me, which reads claims, not per-owner rows). The first owned-resource
/// slice — Documents (08 build order) — must land with this test enabled. The
/// skipped fact keeps the obligation visible in the runner rather than buried in a
/// doc's open-items list.
/// </summary>
public sealed class OwnershipTests
{
    [Fact(Skip = "Pending the first owned resource (Documents module). Enable when GET " +
                 "/api/v1/documents/{id} exists: owner A creates a document, owner B " +
                 "requests it, expect 404 (not 403/200). See 05-security.md.")]
    public void CrossOwnerAccessToOwnedResource_Returns404()
    {
        // Arrange: register owner A and owner B (RegisterAndAuthenticateAsync).
        // Act:     A creates a resource; B GETs it with B's bearer token.
        // Assert:  response is 404 NotFound — never 403, never 200.
    }
}
