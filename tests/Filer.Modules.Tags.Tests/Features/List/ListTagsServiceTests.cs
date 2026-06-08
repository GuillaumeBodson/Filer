using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Features.List;
using Filer.Modules.Tags.Persistence;
using Filer.Modules.Tags.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Tags.Tests.Features.List;

/// <summary>
/// The list-tags slice's success path and its one <c>Error</c> path, at its
/// designed seam (12-testing-strategy.md) — including the entity → DTO mapping
/// the issue pins a unit test on (#46). The owner-scoped EF query and the HTTP
/// status mapping are exercised in Filer.IntegrationTests.
/// </summary>
public sealed class ListTagsServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly Mock<ITagStore> _tags = new(MockBehavior.Strict);

    private ListTagsService CreateSut(ICurrentUser? caller = null) =>
        new(
            _tags.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<ListTagsService>.Instance);

    private void StoreReturns(params Tag[] tags) =>
        _tags
            .Setup(t => t.ListAsync(OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

    /// <summary>A tag owned by the test's caller, as the store would return it.</summary>
    private static Tag OwnedTag(string name) => new()
    {
        OwnerId = OwnerId,
        Name = name,
        CreatedAt = new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 6, 7, 12, 30, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStore()
    {
        Result<IReadOnlyList<TagListItemResponse>> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _tags.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_MapsEachTagToItsDtoPreservingTheStoreOrder()
    {
        // The mapping the issue pins (#46): every field projected, the entity's
        // OwnerId never surfaces, and the store's name order is preserved.
        Tag archived = OwnedTag("archived");
        Tag urgent = OwnedTag("urgent");
        StoreReturns(archived, urgent);

        Result<IReadOnlyList<TagListItemResponse>> result =
            await CreateSut().HandleAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(t => t.Name).Should().ContainInOrder("archived", "urgent");

        TagListItemResponse mapped = result.Value.Single(t => t.Name == "urgent");
        mapped.Id.Should().Be(urgent.Id);
        mapped.Name.Should().Be(urgent.Name);
        mapped.CreatedAt.Should().Be(urgent.CreatedAt);
        mapped.UpdatedAt.Should().Be(urgent.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_WithoutAnyTags_ReturnsAnEmptyList()
    {
        StoreReturns();

        Result<IReadOnlyList<TagListItemResponse>> result =
            await CreateSut().HandleAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
