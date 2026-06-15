using System.Text.Json;
using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.ApplyDocumentAnalysis;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.Modules.Tags.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.ApplyDocumentAnalysis;

/// <summary>
/// The apply-suggestions slice (#55) at its seams (12-testing-strategy.md):
/// all/some/none of the suggestions, the AiSuggested source on created rows,
/// idempotent re-apply, and every Error path — including the uniform 404 for
/// cross-owner documents and stale suggestion folders (05-security.md).
/// </summary>
public sealed class ApplyDocumentAnalysisServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid SuggestedFolderId = Guid.NewGuid();
    private static readonly Guid InvoicesTagId = Guid.NewGuid();
    private static readonly Guid YearTagId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Serialized exactly like the worker (#53): Web defaults, no extra converters.</summary>
    private static readonly JsonSerializerOptions WriterOptions = new(JsonSerializerDefaults.Web);

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);
    private readonly Mock<IAnalysisJobReader> _jobs = new(MockBehavior.Strict);
    private readonly Mock<ITagNameResolver> _tagNames = new(MockBehavior.Strict);

    private ApplyDocumentAnalysisService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            _jobs.Object,
            _tagNames.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<ApplyDocumentAnalysisService>.Instance);

    private static Document OwnedDocument() => new()
    {
        Id = DocumentId, OwnerId = OwnerId, FolderId = null, FileName = "d.pdf",
        ContentType = "application/pdf", SizeBytes = 1, StorageKey = new string('a', 64),
        ContentHash = new string('b', 64), Status = DocumentStatus.Uploaded,
    };

    /// <summary>The canonical stored result: an existing folder and two tag suggestions.</summary>
    private static DocumentAnalysisResult StoredResult(Guid? existingFolderId = null) => new(
        new FolderSuggestion(existingFolderId ?? SuggestedFolderId, "Invoices", 0.9),
        [new TagSuggestion("invoices", 0.8), new TagSuggestion("2026", 0.6)],
        []);

    private Document ArrangeOwnedDocument()
    {
        Document document = OwnedDocument();
        _documents.Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        return document;
    }

    private void ArrangeSucceededJob(DocumentAnalysisResult result) =>
        _jobs.Setup(j => j.FindLatestForDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisJobSnapshot(
                JobId, AnalysisJobState.Succeeded, JsonSerializer.Serialize(result, WriterOptions), Now));

    private void ArrangeResolvedTags(params ResolvedTag[] resolved) =>
        _tagNames.Setup(t => t.ResolveOwnedByNamesAsync(
                OwnerId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);

    private void ArrangeCurrentTags(params DocumentTag[] current) =>
        _documents.Setup(d => d.ListTagsForDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);

    private void ArrangeFolderOwned(bool owned = true) =>
        _documents.Setup(d => d.OwnedFolderExistsAsync(OwnerId, SuggestedFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(owned);

    private void ArrangeApply() =>
        _documents.Setup(d => d.ApplyAnalysisAsync(
            It.IsAny<Document?>(),
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    private static ApplyDocumentAnalysisRequest Request(bool applyFolder = false, params string[] tags) =>
        new(applyFolder, tags);

    [Fact]
    public async Task HandleAsync_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(DocumentId, Request(), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTagsArrayMissing_ReturnsValidation()
    {
        var result = await CreateSut().HandleAsync(
            DocumentId, new ApplyDocumentAnalysisRequest(false, Tags: null), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.AnalysisTagsInvalid);
    }

    [Fact]
    public async Task HandleAsync_WhenTagNameBlank_ReturnsValidation()
    {
        var result = await CreateSut().HandleAsync(
            DocumentId, Request(false, "invoices", "  "), CancellationToken.None);

        result.Error!.Code.Should().Be(DocumentsErrorCodes.AnalysisTagsInvalid);
    }

    [Fact]
    public async Task HandleAsync_WhenDocumentNotOwned_ReturnsNotFound_WithoutTouchingJobs()
    {
        _documents.Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        var result = await CreateSut().HandleAsync(DocumentId, Request(), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.DocumentNotFound);
        _jobs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenNoJob_ReturnsAnalysisNotFound()
    {
        ArrangeOwnedDocument();
        _jobs.Setup(j => j.FindLatestForDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnalysisJobSnapshot?)null);

        var result = await CreateSut().HandleAsync(DocumentId, Request(), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.AnalysisNotFound);
    }

    [Theory]
    [InlineData(AnalysisJobState.Queued)]
    [InlineData(AnalysisJobState.Running)]
    [InlineData(AnalysisJobState.Failed)]
    [InlineData(AnalysisJobState.Cancelled)]
    public async Task HandleAsync_WhenLatestJobNotSucceeded_ReturnsAnalysisNotFound(AnalysisJobState state)
    {
        ArrangeOwnedDocument();
        _jobs.Setup(j => j.FindLatestForDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisJobSnapshot(JobId, state, null, null));

        var result = await CreateSut().HandleAsync(DocumentId, Request(), CancellationToken.None);

        result.Error!.Code.Should().Be(DocumentsErrorCodes.AnalysisNotFound);
    }

    [Fact]
    public async Task HandleAsync_WhenTagNotAmongSuggestions_ReturnsValidation()
    {
        ArrangeOwnedDocument();
        ArrangeSucceededJob(StoredResult());

        var result = await CreateSut().HandleAsync(
            DocumentId, Request(false, "invoices", "not-suggested"), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.TagNotSuggested);
    }

    [Fact]
    public async Task HandleAsync_WhenFolderConfirmedButNoneSuggested_ReturnsValidation()
    {
        ArrangeOwnedDocument();
        ArrangeSucceededJob(new DocumentAnalysisResult(null, [new TagSuggestion("invoices", 0.8)], []));

        var result = await CreateSut().HandleAsync(
            DocumentId, Request(applyFolder: true), CancellationToken.None);

        result.Error!.Code.Should().Be(DocumentsErrorCodes.FolderNotSuggested);
    }

    [Fact]
    public async Task HandleAsync_WhenSuggestedFolderIsProposedNew_ReturnsValidation()
    {
        // ExistingFolderId null = a proposed NEW folder; V1 has no cross-module
        // folder creation, so the apply is rejected explicitly rather than half-done.
        ArrangeOwnedDocument();
        ArrangeSucceededJob(new DocumentAnalysisResult(
            new FolderSuggestion(null, "Brand New", 0.7), [], []));

        var result = await CreateSut().HandleAsync(
            DocumentId, Request(applyFolder: true), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.ProposedFolderNotSupported);
    }

    [Fact]
    public async Task HandleAsync_WhenSuggestedFolderGoneOrCrossOwner_ReturnsNotFound()
    {
        // The suggestion may be stale: the folder was deleted — or never the
        // caller's. Both are the same false from the owner-scoped check → 404.
        ArrangeOwnedDocument();
        ArrangeSucceededJob(StoredResult());
        ArrangeFolderOwned(false);

        var result = await CreateSut().HandleAsync(
            DocumentId, Request(applyFolder: true), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.FolderNotFound);
    }

    [Fact]
    public async Task HandleAsync_WhenConfirmedTagNotCreatedYet_ReturnsValidation()
    {
        // "2026" is suggested but the owner has no such tag: V1 has no
        // cross-module tag creation, so the user must create it first.
        ArrangeOwnedDocument();
        ArrangeSucceededJob(StoredResult());
        ArrangeResolvedTags(new ResolvedTag(InvoicesTagId, "invoices"));

        var result = await CreateSut().HandleAsync(
            DocumentId, Request(false, "invoices", "2026"), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.SuggestedTagNotCreated);
    }

    [Fact]
    public async Task HandleAsync_WhenAllSuggestionsConfirmed_AppliesFolderAndTagsAtomically()
    {
        Document document = ArrangeOwnedDocument();
        ArrangeSucceededJob(StoredResult());
        ArrangeFolderOwned();
        ArrangeResolvedTags(new ResolvedTag(InvoicesTagId, "invoices"), new ResolvedTag(YearTagId, "2026"));
        ArrangeCurrentTags();
        ArrangeApply();

        var result = await CreateSut().HandleAsync(
            DocumentId, Request(applyFolder: true, "invoices", "2026"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FolderApplied.Should().BeTrue();
        result.Value.FolderId.Should().Be(SuggestedFolderId);
        document.FolderId.Should().Be(SuggestedFolderId);
        document.UpdatedAt.Should().Be(Now);

        // One store call carries both halves: the moved document and the new rows,
        // every one of them Source = AiSuggested (02-data-model.md).
        _documents.Verify(d => d.ApplyAnalysisAsync(
            It.Is<Document?>(doc => doc != null && doc.FolderId == SuggestedFolderId),
            It.Is<IReadOnlyCollection<DocumentTag>>(rows =>
                rows.Count == 2
                && rows.All(r => r.Source == DocumentTagSource.AiSuggested && r.DocumentId == DocumentId && r.CreatedAt == Now)
                && rows.Select(r => r.TagId).OrderBy(id => id).SequenceEqual(
                    new[] { InvoicesTagId, YearTagId }.OrderBy(id => id))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenSomeSuggestionsConfirmed_AppliesOnlyThose()
    {
        ArrangeOwnedDocument();
        ArrangeSucceededJob(StoredResult());
        ArrangeResolvedTags(new ResolvedTag(InvoicesTagId, "invoices"));
        ArrangeCurrentTags();
        ArrangeApply();

        var result = await CreateSut().HandleAsync(
            DocumentId, Request(false, "invoices"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FolderApplied.Should().BeFalse();
        result.Value.FolderId.Should().BeNull();

        // Folder not confirmed: the document is not part of the write.
        _documents.Verify(d => d.ApplyAnalysisAsync(
            null,
            It.Is<IReadOnlyCollection<DocumentTag>>(rows =>
                rows.Count == 1 && rows.Single().TagId == InvoicesTagId
                && rows.Single().Source == DocumentTagSource.AiSuggested),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenNothingConfirmed_SucceedsWithoutWriting()
    {
        // "A user may accept all, some, or none" (06): none is a success, no write.
        ArrangeOwnedDocument();
        ArrangeSucceededJob(StoredResult());
        ArrangeResolvedTags();
        ArrangeCurrentTags();

        var result = await CreateSut().HandleAsync(DocumentId, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FolderApplied.Should().BeFalse();
        result.Value.Tags.Should().BeEmpty();
        _documents.Verify(d => d.ApplyAnalysisAsync(
            It.IsAny<Document?>(),
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(DocumentTagSource.AiSuggested)]
    [InlineData(DocumentTagSource.User)]
    public async Task HandleAsync_WhenTagAlreadyAssociated_IsIdempotentNoOp(DocumentTagSource existingSource)
    {
        // Re-applying an already-associated tag inserts nothing and never demotes
        // a User row back to AiSuggested.
        ArrangeOwnedDocument();
        ArrangeSucceededJob(StoredResult());
        ArrangeResolvedTags(new ResolvedTag(InvoicesTagId, "invoices"));
        ArrangeCurrentTags(new DocumentTag
        {
            DocumentId = DocumentId, TagId = InvoicesTagId, Source = existingSource,
            CreatedAt = Now.AddDays(-1),
        });

        var result = await CreateSut().HandleAsync(
            DocumentId, Request(false, "invoices"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tags.Should().ContainSingle(t => t.TagId == InvoicesTagId)
            .Which.Source.Should().Be(existingSource.ToString());
        _documents.Verify(d => d.ApplyAnalysisAsync(
            It.IsAny<Document?>(),
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MatchesSuggestionsCaseInsensitively()
    {
        // "INVOICES" confirms the suggestion "invoices" (06: the user confirms a
        // suggestion, not a case-exact string).
        ArrangeOwnedDocument();
        ArrangeSucceededJob(StoredResult());
        ArrangeResolvedTags(new ResolvedTag(InvoicesTagId, "Invoices"));
        ArrangeCurrentTags();
        ArrangeApply();

        var result = await CreateSut().HandleAsync(
            DocumentId, Request(false, "INVOICES"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _documents.Verify(d => d.ApplyAnalysisAsync(
            null,
            It.Is<IReadOnlyCollection<DocumentTag>>(rows =>
                rows.Count == 1 && rows.Single().TagId == InvoicesTagId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
