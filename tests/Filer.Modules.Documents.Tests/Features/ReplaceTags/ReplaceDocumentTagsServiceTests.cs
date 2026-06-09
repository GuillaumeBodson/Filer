using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.ReplaceTags;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.Modules.Tags.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.ReplaceTags;

/// <summary>
/// The replace-tags slice's success paths, the AiSuggested preserve-vs-promote
/// matrix, and every <c>Error</c> path, at its designed seams
/// (12-testing-strategy.md). The owner-scoped EF lookups, the join persistence and
/// the HTTP mapping are exercised in Filer.IntegrationTests.
/// </summary>
public sealed class ReplaceDocumentTagsServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid TagA = Guid.NewGuid();
    private static readonly Guid TagB = Guid.NewGuid();
    private static readonly Guid TagC = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);
    private readonly Mock<ITagOwnershipChecker> _tagOwnership = new(MockBehavior.Strict);

    private ReplaceDocumentTagsService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            _tagOwnership.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<ReplaceDocumentTagsService>.Instance);

    private static Document OwnedDocument() => new()
    {
        Id = DocumentId,
        OwnerId = OwnerId,
        FileName = "doc.pdf",
        ContentType = "application/pdf",
        SizeBytes = 1,
        StorageKey = new string('a', 64),
        ContentHash = new string('b', 64),
        Status = DocumentStatus.Uploaded,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1),
    };

    private static DocumentTag Row(Guid tagId, DocumentTagSource source) => new()
    {
        DocumentId = DocumentId,
        TagId = tagId,
        Source = source,
        CreatedAt = Now.AddDays(-1),
    };

    private void ArrangeOwnedDocument() =>
        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnedDocument());

    private void ArrangeTagsOwned(bool owned = true) =>
        _tagOwnership
            .Setup(t => t.OwnsAllTagsAsync(OwnerId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(owned);

    private void ArrangeCurrentTags(params DocumentTag[] rows)
    {
        var first = true;
        _documents
            .Setup(d => d.ListTagsForDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // First call returns the pre-state; the post-apply re-read returns
                // whatever the captured ApplyTagChanges produced.
                if (first)
                {
                    first = false;
                    return rows;
                }
                return _applied;
            });
    }

    private IReadOnlyList<DocumentTag> _applied = [];

    private void CaptureApply(IReadOnlyList<DocumentTag> currentRows) =>
        _documents
            .Setup(d => d.ApplyTagChangesAsync(
                It.IsAny<IReadOnlyCollection<DocumentTag>>(),
                It.IsAny<IReadOnlyCollection<DocumentTag>>(),
                It.IsAny<IReadOnlyCollection<DocumentTag>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<DocumentTag>, IReadOnlyCollection<DocumentTag>, IReadOnlyCollection<DocumentTag>, CancellationToken>(
                (insert, promote, remove, _) =>
                {
                    var result = currentRows.ToList();
                    result.RemoveAll(r => remove.Any(x => x.TagId == r.TagId));
                    // promotes mutate in place (same reference), already reflected.
                    result.AddRange(insert);
                    _applied = result;
                })
            .Returns(Task.CompletedTask);

    [Fact]
    public async Task HandleAsync_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest([TagA]), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        _documents.VerifyNoOtherCalls();
        _tagOwnership.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTagIdsNull_ReturnsValidation()
    {
        var result = await CreateSut()
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest(null), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.TagIdsInvalid);
        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTagIdsContainEmptyGuid_ReturnsValidation()
    {
        var result = await CreateSut()
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest([Guid.Empty]), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.TagIdsInvalid);
    }

    [Fact]
    public async Task HandleAsync_WhenDocumentNotOwned_ReturnsNotFound()
    {
        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        var result = await CreateSut()
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest([TagA]), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.DocumentNotFound);
    }

    [Fact]
    public async Task HandleAsync_WhenATagNotOwned_ReturnsNotFound()
    {
        ArrangeOwnedDocument();
        ArrangeTagsOwned(false);

        var result = await CreateSut()
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest([TagA]), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.TagNotFound);
    }

    [Fact]
    public async Task HandleAsync_InsertsNewUserTags_WhenNoneExisted()
    {
        ArrangeOwnedDocument();
        ArrangeTagsOwned();
        var current = new List<DocumentTag>();
        ArrangeCurrentTags();
        CaptureApply(current);

        var result = await CreateSut()
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest([TagA, TagB]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _documents.Verify(d => d.ApplyTagChangesAsync(
            It.Is<IReadOnlyCollection<DocumentTag>>(i => i.Count == 2 && i.All(r => r.Source == DocumentTagSource.User)),
            It.Is<IReadOnlyCollection<DocumentTag>>(p => p.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(r => r.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_RemovesUserTagsNotInNewSet_AndPreservesAiSuggested()
    {
        ArrangeOwnedDocument();
        ArrangeTagsOwned();
        // Current: TagA=User (will be removed, not in new set), TagC=AiSuggested
        // (preserved, not in new set), plus we add TagB.
        var existing = new List<DocumentTag> { Row(TagA, DocumentTagSource.User), Row(TagC, DocumentTagSource.AiSuggested) };
        ArrangeCurrentTags(existing.ToArray());
        CaptureApply(existing);

        var result = await CreateSut()
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest([TagB]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _documents.Verify(d => d.ApplyTagChangesAsync(
            It.Is<IReadOnlyCollection<DocumentTag>>(i => i.Count == 1 && i.Single().TagId == TagB),
            It.Is<IReadOnlyCollection<DocumentTag>>(p => p.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(rm => rm.Count == 1 && rm.Single().TagId == TagA),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PromotesAiSuggested_WhenItsTagIsInNewSet()
    {
        ArrangeOwnedDocument();
        ArrangeTagsOwned();
        var aiRow = Row(TagA, DocumentTagSource.AiSuggested);
        var existing = new List<DocumentTag> { aiRow };
        ArrangeCurrentTags(existing.ToArray());
        CaptureApply(existing);

        var result = await CreateSut()
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest([TagA]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // The existing AiSuggested row is promoted to User (an update, not an insert).
        aiRow.Source.Should().Be(DocumentTagSource.User);
        _documents.Verify(d => d.ApplyTagChangesAsync(
            It.Is<IReadOnlyCollection<DocumentTag>>(i => i.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(p => p.Count == 1 && p.Single().TagId == TagA && p.Single().Source == DocumentTagSource.User),
            It.Is<IReadOnlyCollection<DocumentTag>>(rm => rm.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_LeavesExistingUserTagUntouched_WhenStillInNewSet()
    {
        ArrangeOwnedDocument();
        ArrangeTagsOwned();
        var existing = new List<DocumentTag> { Row(TagA, DocumentTagSource.User) };
        ArrangeCurrentTags(existing.ToArray());
        CaptureApply(existing);

        var result = await CreateSut()
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest([TagA]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _documents.Verify(d => d.ApplyTagChangesAsync(
            It.Is<IReadOnlyCollection<DocumentTag>>(i => i.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(p => p.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(rm => rm.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_EmptySet_ClearsUserTags_ButKeepsAiSuggested()
    {
        ArrangeOwnedDocument();
        ArrangeTagsOwned();
        var existing = new List<DocumentTag>
        {
            Row(TagA, DocumentTagSource.User),
            Row(TagC, DocumentTagSource.AiSuggested),
        };
        ArrangeCurrentTags(existing.ToArray());
        CaptureApply(existing);

        var result = await CreateSut()
            .HandleAsync(DocumentId, new ReplaceDocumentTagsRequest([]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _documents.Verify(d => d.ApplyTagChangesAsync(
            It.Is<IReadOnlyCollection<DocumentTag>>(i => i.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(p => p.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(rm => rm.Count == 1 && rm.Single().TagId == TagA),
            It.IsAny<CancellationToken>()), Times.Once);
        // The AiSuggested row survives in the returned set.
        result.Value.Tags.Should().ContainSingle(t => t.TagId == TagC && t.Source == "AiSuggested");
    }
}
