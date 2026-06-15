using System.Text.Json;
using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.GetDocumentAnalysis;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.GetDocumentAnalysis;

/// <summary>
/// The analysis-status slice (#54) at its seams (12-testing-strategy.md): every
/// job state maps to the documented response shape, a terminal failure surfaces
/// as unavailable with no internal detail, and a missing or cross-owner document
/// is the uniform 404 (05-security.md).
/// </summary>
public sealed class GetDocumentAnalysisServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly DateTimeOffset CompletedAt = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Serialized exactly like the worker (#53): Web defaults, no extra converters.</summary>
    private static readonly JsonSerializerOptions WriterOptions = new(JsonSerializerDefaults.Web);

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);
    private readonly Mock<IAnalysisJobReader> _jobs = new(MockBehavior.Strict);

    private GetDocumentAnalysisService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            _jobs.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<GetDocumentAnalysisService>.Instance);

    private static Document OwnedDocument() => new()
    {
        Id = DocumentId, OwnerId = OwnerId, FileName = "d.pdf", ContentType = "application/pdf",
        SizeBytes = 1, StorageKey = new string('a', 64), ContentHash = new string('b', 64),
        Status = DocumentStatus.Uploaded,
    };

    private void ArrangeOwnedDocument() =>
        _documents.Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnedDocument());

    private void ArrangeLatestJob(AnalysisJobSnapshot? snapshot) =>
        _jobs.Setup(j => j.FindLatestForDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

    [Fact]
    public async Task HandleAsync_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var result = await CreateSut(StubCurrentUser.Anonymous).HandleAsync(DocumentId, CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        _documents.VerifyNoOtherCalls();
        _jobs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenDocumentNotOwned_ReturnsNotFound_WithoutTouchingJobs()
    {
        // Cross-owner and missing are the same null from the owner-scoped lookup,
        // and the (not owner-scoped) job reader must never be consulted for it.
        _documents.Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        var result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.DocumentNotFound);
        _jobs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenNoJob_ReturnsNoneWithoutSuggestions()
    {
        ArrangeOwnedDocument();
        ArrangeLatestJob(null);

        var result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(DocumentAnalysisResponse.Unavailable(DocumentId));
        result.Value.Status.Should().Be("None");
    }

    [Fact]
    public async Task HandleAsync_WhenJobCancelled_IsTreatedAsUnavailable()
    {
        ArrangeOwnedDocument();
        ArrangeLatestJob(new AnalysisJobSnapshot(JobId, AnalysisJobState.Cancelled, null, CompletedAt));

        var result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("None");
        result.Value.JobId.Should().BeNull();
        result.Value.Suggestions.Should().BeNull();
    }

    [Theory]
    [InlineData(AnalysisJobState.Queued, "Queued")]
    [InlineData(AnalysisJobState.Running, "Running")]
    public async Task HandleAsync_WhenJobPending_ReturnsStatusWithoutSuggestions(
        AnalysisJobState state, string expectedStatus)
    {
        ArrangeOwnedDocument();
        ArrangeLatestJob(new AnalysisJobSnapshot(JobId, state, null, null));

        var result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(expectedStatus);
        result.Value.JobId.Should().Be(JobId);
        result.Value.Suggestions.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenJobFailedTerminally_SurfacesUnavailable_WithoutErrorDetail()
    {
        // The snapshot deliberately carries no error text (IAnalysisJobReader);
        // the response carries only the bare status (06, Failure Handling).
        ArrangeOwnedDocument();
        ArrangeLatestJob(new AnalysisJobSnapshot(JobId, AnalysisJobState.Failed, null, CompletedAt));

        var result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Failed");
        result.Value.JobId.Should().Be(JobId);
        result.Value.CompletedAt.Should().Be(CompletedAt);
        result.Value.Suggestions.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenJobSucceeded_ReturnsSuggestionsProjectedToDtos()
    {
        Guid folderId = Guid.NewGuid();
        Guid duplicateId = Guid.NewGuid();
        var stored = new DocumentAnalysisResult(
            new FolderSuggestion(folderId, "Invoices", 0.92),
            [new TagSuggestion("invoices", 0.81), new TagSuggestion("2026", 0.6)],
            [new DuplicateSignal(duplicateId, DuplicateKind.ExactContent, 1)]);

        ArrangeOwnedDocument();
        ArrangeLatestJob(new AnalysisJobSnapshot(
            JobId, AnalysisJobState.Succeeded, JsonSerializer.Serialize(stored, WriterOptions), CompletedAt));

        var result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Succeeded");
        result.Value.JobId.Should().Be(JobId);
        result.Value.CompletedAt.Should().Be(CompletedAt);

        DocumentAnalysisSuggestionsResponse suggestions = result.Value.Suggestions!;
        suggestions.SuggestedFolder.Should().Be(
            new AnalysisFolderSuggestionResponse(folderId, "Invoices", 0.92));
        suggestions.SuggestedTags.Should().Equal(
            new AnalysisTagSuggestionResponse("invoices", 0.81),
            new AnalysisTagSuggestionResponse("2026", 0.6));
        suggestions.DuplicateSignals.Should().Equal(
            new AnalysisDuplicateSignalResponse(duplicateId, "ExactContent", 1));
    }

    [Fact]
    public async Task HandleAsync_WhenSucceededResultUnreadable_DegradesToUnavailable()
    {
        ArrangeOwnedDocument();
        ArrangeLatestJob(new AnalysisJobSnapshot(JobId, AnalysisJobState.Succeeded, "{not json", CompletedAt));

        var result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Failed");
        result.Value.Suggestions.Should().BeNull();
    }
}
