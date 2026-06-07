using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.Delete;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.Delete;

/// <summary>
/// The delete slice's success path and every <c>Error</c> path, at its designed
/// seams (12-testing-strategy.md). The owner-scoped EF lookup, the real job
/// cancellation against Postgres, and the HTTP status mapping are exercised in
/// Filer.IntegrationTests.
/// </summary>
public sealed class DeleteDocumentServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);
    private readonly Mock<IBackgroundJobQueue> _jobQueue = new(MockBehavior.Strict);

    private DeleteDocumentService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            _jobQueue.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<DeleteDocumentService>.Instance);

    private static Document OwnedDocument() => new()
    {
        Id = DocumentId,
        OwnerId = OwnerId,
        FolderId = null,
        FileName = "document.pdf",
        ContentType = "application/pdf",
        SizeBytes = 1234,
        StorageKey = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234",
        ContentHash = "1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff",
        Status = DocumentStatus.Uploaded,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1),
    };

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingAnySeam()
    {
        Result result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(DocumentId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _documents.VerifyNoOtherCalls();
        _jobQueue.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenDocumentNotFound_Returns404WithoutWritingOrCancelling()
    {
        // The store is owner-scoped and soft-delete-aware, so missing, cross-owner,
        // and already-deleted all surface here as null — one uniform 404 (05).
        _documents
            .Setup(s => s.FindActiveByIdAsync(OwnerId, DocumentId, CancellationToken.None))
            .ReturnsAsync((Document?)null);

        Result result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.DocumentNotFound);

        _documents.Verify(s => s.FindActiveByIdAsync(OwnerId, DocumentId, CancellationToken.None), Times.Once);
        _documents.VerifyNoOtherCalls();
        _jobQueue.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_OnSuccess_StampsDeletedAtPersistsAndCancelsTheDocumentsJobs()
    {
        Document document = OwnedDocument();
        _documents
            .Setup(s => s.FindActiveByIdAsync(OwnerId, DocumentId, CancellationToken.None))
            .ReturnsAsync(document);
        _documents
            .Setup(s => s.UpdateAsync(document, CancellationToken.None))
            .Returns(Task.CompletedTask);
        _jobQueue
            .Setup(q => q.CancelForDocumentAsync(DocumentId, CancellationToken.None))
            .ReturnsAsync(Result.Success(1));

        Result result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        document.DeletedAt.Should().Be(Now);
        document.UpdatedAt.Should().Be(Now);

        // The delete is persisted before the cancellation is even attempted.
        _documents.Verify(s => s.UpdateAsync(document, CancellationToken.None), Times.Once);
        _jobQueue.Verify(q => q.CancelForDocumentAsync(DocumentId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenDocumentHasNoJobs_ZeroCancelledIsStillSuccess()
    {
        Document document = OwnedDocument();
        _documents
            .Setup(s => s.FindActiveByIdAsync(OwnerId, DocumentId, CancellationToken.None))
            .ReturnsAsync(document);
        _documents
            .Setup(s => s.UpdateAsync(document, CancellationToken.None))
            .Returns(Task.CompletedTask);
        _jobQueue
            .Setup(q => q.CancelForDocumentAsync(DocumentId, CancellationToken.None))
            .ReturnsAsync(Result.Success(0));

        Result result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        document.DeletedAt.Should().Be(Now);
    }

    [Fact]
    public async Task HandleAsync_WhenCancellationFails_PropagatesTheErrorAfterTheDeleteIsDurable()
    {
        // Unreachable by construction (a resolved document never has an empty id),
        // but a seam failure must surface, never be swallowed (13). The soft-delete
        // has already committed by design — see the service's ordering rationale.
        Document document = OwnedDocument();
        Error queueError = Error.Validation("boom", BackgroundJobsErrorCodes.DocumentIdRequired);
        _documents
            .Setup(s => s.FindActiveByIdAsync(OwnerId, DocumentId, CancellationToken.None))
            .ReturnsAsync(document);
        _documents
            .Setup(s => s.UpdateAsync(document, CancellationToken.None))
            .Returns(Task.CompletedTask);
        _jobQueue
            .Setup(q => q.CancelForDocumentAsync(DocumentId, CancellationToken.None))
            .ReturnsAsync(Result.Failure<int>(queueError));

        Result result = await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeSameAs(queueError);

        _documents.Verify(s => s.UpdateAsync(document, CancellationToken.None), Times.Once);
    }
}
