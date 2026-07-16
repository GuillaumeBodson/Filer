using System.Text;
using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Folders.Contracts;
using Filer.Modules.Storage.Contracts;
using Filer.Modules.Tags.Contracts;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Filer.Modules.AiAnalysis.Tests;

/// <summary>
/// The analysis job handler against mocked seams (#53, 12-testing-strategy.md):
/// the success path serializes the provider's result and marks the document
/// Ready; V1 text extraction reads only text/plain and text/markdown, truncated;
/// a deleted document yields the cancel signal; provider throws propagate (the
/// worker owns retry); re-running produces an identical persisted shape.
/// </summary>
public sealed class AnalysisJobHandlerTests
{
    private const string StorageKey = "0123456789abcdef";

    private readonly Mock<IDocumentAnalysisGateway> _documents = new();
    private readonly Mock<IOwnerFolderReader> _folders = new();
    private readonly Mock<IOwnerTagReader> _tags = new();
    private readonly Mock<IFileStorageProvider> _storage = new();
    private readonly Mock<IAIAnalysisProvider> _provider = new();

    private readonly Guid _documentId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly ClaimedAnalysisJob _job;

    private static readonly DocumentAnalysisResult CannedResult = new(
        new FolderSuggestion(ExistingFolderId: null, "Unsorted", 0.3),
        [new TagSuggestion("invoice", 0.5)],
        []);

    public AnalysisJobHandlerTests()
    {
        _job = new ClaimedAnalysisJob(Guid.NewGuid(), _documentId, AttemptCount: 1);

        _folders
            .Setup(f => f.ListActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _documents
            .Setup(d => d.CountActiveByFolderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());
        _tags
            .Setup(t => t.ListNamesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _provider
            .Setup(p => p.AnalyzeAsync(It.IsAny<DocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CannedResult);
    }

    private AnalysisJobHandler CreateSut(int maxChars = 8_000) => new(
        _documents.Object,
        _folders.Object,
        _tags.Object,
        _storage.Object,
        _provider.Object,
        Options.Create(new AiAnalysisOptions { Provider = AiAnalysisOptions.OllamaProviderName }),
        Options.Create(new TextExtractionOptions { MaxChars = maxChars }),
        NullLogger<AnalysisJobHandler>.Instance);

    private void GivenDocument(string fileName, string contentType) =>
        _documents
            .Setup(d => d.FindForAnalysisAsync(_documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDocumentSnapshot(_documentId, _ownerId, fileName, contentType, StorageKey));

    private void GivenBlobContent(string content) =>
        _storage
            .Setup(s => s.OpenReadAsync(StorageKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public void ProviderName_ReturnsTheConfiguredProviderSelector() =>
        CreateSut().ProviderName.Should().Be(
            AiAnalysisOptions.OllamaProviderName,
            "the claim stamps AnalysisJob.Provider with the adapter the configuration selected (#163)");

    [Fact]
    public async Task HandleAsync_OnSuccess_ReturnsTheSerializedResultAndMarksTheDocumentReady()
    {
        GivenDocument("scan.pdf", "application/pdf");

        Result<string?> result = await CreateSut().HandleAsync(_job, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(
            AnalysisJobResultJson.Serialize(CannedResult),
            "the worker persists exactly the shared serialization of the provider's result");
        _documents.Verify(d => d.MarkReadyAsync(_documentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDocumentIsGone_SignalsCancellationWithoutAnalyzing()
    {
        _documents
            .Setup(d => d.FindForAnalysisAsync(_documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnalysisDocumentSnapshot?)null);

        Result<string?> result = await CreateSut().HandleAsync(_job, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(
            BackgroundJobsErrorCodes.DocumentGone,
            "a deleted document cancels the job rather than failing it (06)");
        _provider.Verify(
            p => p.AnalyzeAsync(It.IsAny<DocumentAnalysisRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _documents.Verify(
            d => d.MarkReadyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_PassesTheOwnersFolderTreeWithCountsAndTagsToTheProvider()
    {
        GivenDocument("notes.txt", "text/plain");
        GivenBlobContent("hello");
        Guid parentFolderId = Guid.NewGuid();
        Guid childFolderId = Guid.NewGuid();
        _folders
            .Setup(f => f.ListActiveAsync(_ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new OwnerFolder(childFolderId, "2026", parentFolderId),
                new OwnerFolder(parentFolderId, "Invoices", ParentId: null),
            ]);
        _documents
            .Setup(d => d.CountActiveByFolderAsync(_ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [childFolderId] = 3 });
        _tags
            .Setup(t => t.ListNamesAsync(_ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["finance", "2026"]);

        DocumentAnalysisRequest? captured = null;
        _provider
            .Setup(p => p.AnalyzeAsync(It.IsAny<DocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentAnalysisRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(CannedResult);

        await CreateSut().HandleAsync(_job, CancellationToken.None);

        captured.Should().NotBeNull();
        captured.DocumentId.Should().Be(_documentId);
        captured.OwnerId.Should().Be(_ownerId, "providers key their own owner-scoped lookups on it (#119)");
        captured.FileName.Should().Be("notes.txt");
        captured.ContentType.Should().Be("text/plain");
        captured.ExistingFolders.Should().Equal(
            new ExistingFolder(childFolderId, "2026", parentFolderId, DocumentCount: 3),
            new ExistingFolder(parentFolderId, "Invoices", ParentId: null, DocumentCount: 0));
        captured.ExistingTags.Should().Equal("finance", "2026");
    }

    [Fact]
    public async Task HandleAsync_ScopesEveryContextReadToTheDocumentsOwner()
    {
        // The 404 invariant applied to context-gathering (05, #118): folders and
        // counts are read through owner-scoped seams keyed by the document's owner,
        // so another owner's organisation can never enter the prompt.
        GivenDocument("notes.txt", "text/plain");
        GivenBlobContent("hello");

        await CreateSut().HandleAsync(_job, CancellationToken.None);

        _folders.Verify(f => f.ListActiveAsync(_ownerId, It.IsAny<CancellationToken>()), Times.Once);
        _folders.VerifyNoOtherCalls();
        _documents.Verify(d => d.CountActiveByFolderAsync(_ownerId, It.IsAny<CancellationToken>()), Times.Once);
        _tags.Verify(t => t.ListNamesAsync(_ownerId, It.IsAny<CancellationToken>()), Times.Once);
        _tags.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    [InlineData("text/plain; charset=utf-8")]
    public async Task HandleAsync_ForTextualContent_ExtractsTheBlobAsUtf8Text(string contentType)
    {
        GivenDocument("notes.md", contentType);
        GivenBlobContent("# heading\nbody");

        DocumentAnalysisRequest? captured = null;
        _provider
            .Setup(p => p.AnalyzeAsync(It.IsAny<DocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentAnalysisRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(CannedResult);

        await CreateSut().HandleAsync(_job, CancellationToken.None);

        captured!.Text.Should().Be("# heading\nbody");
    }

    [Fact]
    public async Task HandleAsync_TruncatesExtractedTextToTheConfiguredMaximum()
    {
        GivenDocument("notes.txt", "text/plain");
        GivenBlobContent("hello world");

        DocumentAnalysisRequest? captured = null;
        _provider
            .Setup(p => p.AnalyzeAsync(It.IsAny<DocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentAnalysisRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(CannedResult);

        await CreateSut(maxChars: 5).HandleAsync(_job, CancellationToken.None);

        captured!.Text.Should().Be("hello", "extraction truncates at the configured character ceiling");
    }

    [Fact]
    public async Task HandleAsync_ForNonTextContent_PassesEmptyTextWithoutReadingTheBlob()
    {
        // V1 limitation by design: no PDF/Office extraction — providers work from
        // the file name for everything that is not text/plain or text/markdown.
        GivenDocument("scan.pdf", "application/pdf");

        DocumentAnalysisRequest? captured = null;
        _provider
            .Setup(p => p.AnalyzeAsync(It.IsAny<DocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentAnalysisRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(CannedResult);

        await CreateSut().HandleAsync(_job, CancellationToken.None);

        captured!.Text.Should().BeEmpty();
        _storage.Verify(
            s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenTheProviderThrows_PropagatesWithoutMarkingReady()
    {
        GivenDocument("notes.txt", "text/plain");
        GivenBlobContent("hello");
        _provider
            .Setup(p => p.AnalyzeAsync(It.IsAny<DocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider timeout"));

        Func<Task> act = () => CreateSut().HandleAsync(_job, CancellationToken.None);

        // Infrastructure failures throw; the worker translates them into
        // retry/backoff (06, Reliability) — the handler must not swallow them.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("provider timeout");
        _documents.Verify(
            d => d.MarkReadyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_RunTwice_ProducesTheSamePersistedShape()
    {
        // Idempotency (06, Reliability): a re-run is a single JSONB overwrite with
        // an identical payload, and Ready stays Ready — no duplicate suggestions.
        GivenDocument("notes.txt", "text/plain");
        GivenBlobContent("hello");

        AnalysisJobHandler sut = CreateSut();
        Result<string?> first = await sut.HandleAsync(_job, CancellationToken.None);
        Result<string?> second = await sut.HandleAsync(_job, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().Be(first.Value, "re-running the same job must produce a consistent result");
        _documents.Verify(d => d.MarkReadyAsync(_documentId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
