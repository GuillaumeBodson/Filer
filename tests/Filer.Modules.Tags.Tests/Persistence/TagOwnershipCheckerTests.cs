using Filer.Modules.Tags.Persistence;
using FluentAssertions;
using Moq;
using Xunit;

namespace Filer.Modules.Tags.Tests.Persistence;

/// <summary>
/// The cross-module ownership adapter (ADR-009): every distinct id must resolve to
/// an owned tag. The owner-scoped count itself runs against real Postgres in
/// Filer.IntegrationTests; here the all-vs-some logic is pinned at the seam.
/// </summary>
public sealed class TagOwnershipCheckerTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly Mock<ITagStore> _tags = new(MockBehavior.Strict);

    private TagOwnershipChecker CreateSut() => new(_tags.Object);

    [Fact]
    public async Task OwnsAllTagsAsync_EmptySet_IsTrue_WithoutQuerying()
    {
        bool result = await CreateSut().OwnsAllTagsAsync(OwnerId, [], CancellationToken.None);

        result.Should().BeTrue();
        _tags.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnsAllTagsAsync_WhenAllOwned_IsTrue()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        _tags.Setup(t => t.CountOwnedAsync(OwnerId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        bool result = await CreateSut().OwnsAllTagsAsync(OwnerId, [a, b], CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task OwnsAllTagsAsync_WhenSomeMissing_IsFalse()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        _tags.Setup(t => t.CountOwnedAsync(OwnerId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        bool result = await CreateSut().OwnsAllTagsAsync(OwnerId, [a, b], CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task OwnsAllTagsAsync_DeduplicatesBeforeCounting()
    {
        Guid a = Guid.NewGuid();
        // A repeated id is one distinct target; one owned row satisfies it.
        _tags.Setup(t => t.CountOwnedAsync(OwnerId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        bool result = await CreateSut().OwnsAllTagsAsync(OwnerId, [a, a], CancellationToken.None);

        result.Should().BeTrue();
    }
}
