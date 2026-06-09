using Filer.Modules.Documents.Contracts;
using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Features.Delete;
using Filer.Modules.Tags.Persistence;
using Filer.Modules.Tags.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Tags.Tests.Features.Delete;

/// <summary>
/// The delete-tag slice's success path and every <c>Error</c> path at its designed
/// seam (12-testing-strategy.md): the uniform 404 for missing/cross-owner tags,
/// the unauthenticated short-circuit, and the associations-first ordering of the
/// cross-module cascade (ADR-009). The owner-scoped EF delete and the HTTP status
/// mapping are exercised in Filer.IntegrationTests.
/// </summary>
public sealed class DeleteTagServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 9, 14, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITagStore> _tags = new(MockBehavior.Strict);
    private readonly Mock<IDocumentTagRemover> _associations = new(MockBehavior.Strict);

    private DeleteTagService CreateSut(ICurrentUser? caller = null) =>
        new(
            _tags.Object,
            _associations.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<DeleteTagService>.Instance);

    /// <summary>A tag owned by the test's caller, as the store would return it.</summary>
    private static Tag OwnedTag(string name) => new()
    {
        OwnerId = OwnerId,
        Name = name,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1),
    };

    private void StoreFinds(Tag tag) =>
        _tags
            .Setup(t => t.FindByIdAsync(OwnerId, tag.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStores()
    {
        Result result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _tags.VerifyNoOtherCalls();
        _associations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTheTagIsNotOwned_ReturnsNotFoundWithoutRemovingAnything()
    {
        // Missing and cross-owner are indistinguishable through the owner-scoped
        // lookup (05-security.md) — a repeat delete lands here too.
        var tagId = Guid.NewGuid();
        _tags
            .Setup(t => t.FindByIdAsync(OwnerId, tagId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tag?)null);

        Result result = await CreateSut().HandleAsync(tagId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(TagsErrorCodes.NotFound);

        _associations.VerifyNoOtherCalls();
        _tags.Verify(
            t => t.DeleteAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenTagIsOwned_RemovesAssociationsBeforeDeletingTheTag()
    {
        Tag tag = OwnedTag("urgent");
        StoreFinds(tag);

        // Record call order so the associations-first ordering is asserted, not
        // assumed: the join rows must be gone before the tag they point at.
        var order = new List<string>();
        _associations
            .Setup(a => a.RemoveAllForTagAsync(OwnerId, tag.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("associations"))
            .Returns(Task.CompletedTask);
        _tags
            .Setup(t => t.DeleteAsync(tag, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("tag"))
            .Returns(Task.CompletedTask);

        Result result = await CreateSut().HandleAsync(tag.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Should().Equal("associations", "tag");

        _associations.Verify(
            a => a.RemoveAllForTagAsync(OwnerId, tag.Id, It.IsAny<CancellationToken>()), Times.Once);
        _tags.Verify(t => t.DeleteAsync(tag, It.IsAny<CancellationToken>()), Times.Once);
    }
}
