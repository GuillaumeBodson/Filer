using FluentAssertions;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Domain;
using Filer.SharedKernel.Results;
using Xunit;

namespace Filer.SharedKernel.Tests.Authorization;

/// <summary>
/// The ownership guard is the shared enforcement point reused by every protected
/// slice (05-security.md). Its branches are behaviour, not edge cases: ownership
/// match, cross-owner, missing resource, and unauthenticated caller must each be
/// pinned down — cross-owner and missing both collapse to not-found so the API never
/// leaks existence (12-testing-strategy.md).
/// </summary>
public sealed class OwnershipGuardTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Require_WhenCallerOwnsResource_ReturnsSuccessWithResource()
    {
        var resource = new OwnedResource(OwnerId);

        Result<OwnedResource> result = OwnershipGuard.Require(Authenticated(OwnerId), resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(resource);
    }

    [Fact]
    public void Require_WhenCallerIsNotTheOwner_ReturnsNotFound()
    {
        var resource = new OwnedResource(OwnerId);

        Result<OwnedResource> result = OwnershipGuard.Require(Authenticated(OtherId), resource);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Require_WhenResourceDoesNotExist_ReturnsNotFound()
    {
        Result<OwnedResource> result = OwnershipGuard.Require<OwnedResource>(Authenticated(OwnerId), null);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Require_WhenCallerIsNotAuthenticated_ReturnsNotFound()
    {
        var resource = new OwnedResource(OwnerId);

        Result<OwnedResource> result = OwnershipGuard.Require(Unauthenticated(), resource);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Require_WhenCurrentUserIsNull_Throws()
    {
        var resource = new OwnedResource(OwnerId);

        Action act = () => OwnershipGuard.Require(currentUser: null!, resource);

        act.Should().Throw<ArgumentNullException>();
    }

    private static StubCurrentUser Authenticated(Guid id) => new(isAuthenticated: true, id);

    private static StubCurrentUser Unauthenticated() => new(isAuthenticated: false, Guid.Empty);

    private sealed record OwnedResource(Guid OwnerId, Guid? TenantId = null) : IOwnedEntity;

    private sealed class StubCurrentUser(bool isAuthenticated, Guid id) : ICurrentUser
    {
        public bool IsAuthenticated { get; } = isAuthenticated;

        public Guid Id { get; } = id;

        public Guid? TenantId => null;
    }
}
