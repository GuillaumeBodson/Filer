using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.AddTag;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.Modules.Tags.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.AddTag;

/// <summary>
/// The add-tag slice: upsert (insert / promote / idempotent) and every Error path,
/// at its seams (12-testing-strategy.md).
/// </summary>
public sealed class AddDocumentTagServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid TagA = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);
    private readonly Mock<ITagOwnershipChecker> _tagOwnership = new(MockBehavior.Strict);

    private AddDocumentTagService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            _tagOwnership.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<AddDocumentTagService>.Instance);

    private static Document OwnedDocument() => new()
    {
        Id = DocumentId, OwnerId = OwnerId, FileName = "d.pdf", ContentType = "application/pdf",
        SizeBytes = 1, StorageKey = new string('a', 64), ContentHash = new string('b', 64),
        Status = DocumentStatus.Uploaded, CreatedAt = Now, UpdatedAt = Now,
    };

    private static DocumentTag Row(DocumentTagSource source) =>
        new() { DocumentId = DocumentId, TagId = TagA, Source = source, CreatedAt = Now.AddDays(-1) };

    private void ArrangeOwnedDocument() =>
        _documents.Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnedDocument());

    private void ArrangeTagOwned(bool owned = true) =>
        _tagOwnership.Setup(t => t.OwnsAllTagsAsync(OwnerId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(owned);

    private void ArrangeCurrentTags(DocumentTag[] before, DocumentTag[] after)
    {
        var first = true;
        _documents.Setup(d => d.ListTagsForDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { if (first) { first = false; return before; } return after; });
    }

    private void ArrangeApply() =>
        _documents.Setup(d => d.ApplyTagChangesAsync(
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    [Fact]
    public async Task HandleAsync_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var result = await CreateSut(StubCurrentUser.Anonymous).HandleAsync(DocumentId, TagA, CancellationToken.None);
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenDocumentNotOwned_ReturnsNotFound()
    {
        _documents.Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        var result = await CreateSut().HandleAsync(DocumentId, TagA, CancellationToken.None);
        result.Error!.Code.Should().Be(DocumentsErrorCodes.DocumentNotFound);
    }

    [Fact]
    public async Task HandleAsync_WhenTagNotOwned_ReturnsNotFound()
    {
        ArrangeOwnedDocument();
        ArrangeTagOwned(false);
        var result = await CreateSut().HandleAsync(DocumentId, TagA, CancellationToken.None);
        result.Error!.Code.Should().Be(DocumentsErrorCodes.TagNotFound);
    }

    [Fact]
    public async Task HandleAsync_WhenAbsent_InsertsUserRow()
    {
        ArrangeOwnedDocument();
        ArrangeTagOwned();
        ArrangeCurrentTags([], [Row(DocumentTagSource.User)]);
        ArrangeApply();

        var result = await CreateSut().HandleAsync(DocumentId, TagA, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _documents.Verify(d => d.ApplyTagChangesAsync(
            It.Is<IReadOnlyCollection<DocumentTag>>(i => i.Count == 1 && i.Single().Source == DocumentTagSource.User),
            It.Is<IReadOnlyCollection<DocumentTag>>(p => p.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(rm => rm.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAiSuggested_PromotesToUser()
    {
        ArrangeOwnedDocument();
        ArrangeTagOwned();
        var ai = Row(DocumentTagSource.AiSuggested);
        ArrangeCurrentTags([ai], [Row(DocumentTagSource.User)]);
        ArrangeApply();

        var result = await CreateSut().HandleAsync(DocumentId, TagA, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ai.Source.Should().Be(DocumentTagSource.User);
        _documents.Verify(d => d.ApplyTagChangesAsync(
            It.Is<IReadOnlyCollection<DocumentTag>>(i => i.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(p => p.Count == 1 && p.Single().Source == DocumentTagSource.User),
            It.Is<IReadOnlyCollection<DocumentTag>>(rm => rm.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyUser_IsIdempotent_NoWrite()
    {
        ArrangeOwnedDocument();
        ArrangeTagOwned();
        ArrangeCurrentTags([Row(DocumentTagSource.User)], [Row(DocumentTagSource.User)]);
        // No ArrangeApply: ApplyTagChangesAsync must not be called.

        var result = await CreateSut().HandleAsync(DocumentId, TagA, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _documents.Verify(d => d.ApplyTagChangesAsync(
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
