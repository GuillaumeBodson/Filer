using System.Security.Cryptography;
using System.Text;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.Upload;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.Modules.Storage.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.Upload;

/// <summary>
/// The upload slice's success path and every <c>Error</c> path, at its designed
/// seams (12-testing-strategy.md). The EF store and the real HTTP status mapping
/// are exercised in Filer.IntegrationTests.
/// </summary>
public sealed class UploadDocumentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 fake but plausible content");

    private const string StorageKey = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234";

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);
    private readonly Mock<IFileStorageProvider> _storage = new(MockBehavior.Strict);
    private readonly Mock<IBackgroundJobQueue> _jobQueue = new(MockBehavior.Strict);
    private readonly DocumentsOptions _options = new();

    private UploadDocumentService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            _storage.Object,
            _jobQueue.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            Options.Create(_options),
            new FixedClock(Now),
            NullLogger<UploadDocumentService>.Instance);

    private static UploadDocumentCommand PdfCommand(byte[]? bytes = null, string fileName = "invoice.pdf") =>
        Command(bytes ?? PdfBytes, fileName, "application/pdf");

    private static UploadDocumentCommand Command(byte[] bytes, string fileName, string contentType) =>
        new(fileName, contentType, bytes.Length, new MemoryStream(bytes));

    private static string Sha256Lower(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Fact]
    public async Task HandleAsync_WithValidFile_PersistsDocumentQueuesJobAndReturnsMetadata()
    {
        Guid jobId = Guid.NewGuid();
        Document? added = null;

        _documents
            .Setup(d => d.FindActiveByContentHashAsync(OwnerId, Sha256Lower(PdfBytes), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        _documents
            .Setup(d => d.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((doc, _) => added = doc)
            .Returns(Task.CompletedTask);
        _storage
            .Setup(s => s.SaveAsync(It.IsAny<Stream>(), "application/pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorageKey);
        _jobQueue
            .Setup(q => q.EnqueueAnalysisAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(jobId));

        Result<UploadDocumentResult> result = await CreateSut().HandleAsync(PdfCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsDuplicate.Should().BeFalse();

        UploadDocumentResponse response = result.Value.Document!;
        response.FileName.Should().Be("invoice.pdf");
        response.ContentType.Should().Be("application/pdf");
        response.SizeBytes.Should().Be(PdfBytes.Length);
        response.ContentHash.Should().Be(Sha256Lower(PdfBytes));
        response.Status.Should().Be("Uploaded");
        response.CreatedAt.Should().Be(Now);
        response.AnalysisJobId.Should().Be(jobId);

        added.Should().NotBeNull();
        added!.OwnerId.Should().Be(OwnerId);
        added.StorageKey.Should().Be(StorageKey);
        added.Status.Should().Be(DocumentStatus.Uploaded);
        added.DeletedAt.Should().BeNull();
        response.Id.Should().Be(added.Id);

        _jobQueue.Verify(q => q.EnqueueAnalysisAsync(added.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingAnySeam()
    {
        Result<UploadDocumentResult> result =
            await CreateSut(StubCurrentUser.Anonymous).HandleAsync(PdfCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task HandleAsync_WhenFileEmpty_ReturnsFileRequiredValidationError()
    {
        Result<UploadDocumentResult> result =
            await CreateSut().HandleAsync(PdfCommand(bytes: []), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.FileRequired);
    }

    [Fact]
    public async Task HandleAsync_WhenFileNameMissing_ReturnsFileNameInvalidValidationError()
    {
        Result<UploadDocumentResult> result =
            await CreateSut().HandleAsync(PdfCommand(fileName: "  "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.FileNameInvalid);
    }

    [Fact]
    public async Task HandleAsync_WhenFileExceedsConfiguredMaximum_ReturnsPayloadTooLarge()
    {
        _options.MaxUploadBytes = PdfBytes.Length - 1;

        Result<UploadDocumentResult> result =
            await CreateSut().HandleAsync(PdfCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.PayloadTooLarge);
        result.Error.Code.Should().Be(DocumentsErrorCodes.FileTooLarge);
    }

    [Fact]
    public async Task HandleAsync_WhenContentTypeOutsideAllowList_ReturnsUnsupportedFileType()
    {
        Result<UploadDocumentResult> result = await CreateSut()
            .HandleAsync(Command(PdfBytes, "archive.zip", "application/zip"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.UnsupportedMediaType);
        result.Error.Code.Should().Be(DocumentsErrorCodes.UnsupportedFileType);
    }

    [Fact]
    public async Task HandleAsync_WhenMagicBytesContradictDeclaredType_ReturnsContentTypeMismatch()
    {
        byte[] notAPng = Encoding.ASCII.GetBytes("this is plainly not a png");

        Result<UploadDocumentResult> result = await CreateSut()
            .HandleAsync(Command(notAPng, "image.png", "image/png"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.UnsupportedMediaType);
        result.Error.Code.Should().Be(DocumentsErrorCodes.ContentTypeMismatch);
    }

    [Fact]
    public async Task HandleAsync_WhenOwnedDocumentWithSameHashExists_ReturnsDuplicateWithoutStoringBytes()
    {
        var existing = new Document { OwnerId = OwnerId, ContentHash = Sha256Lower(PdfBytes) };

        _documents
            .Setup(d => d.FindActiveByContentHashAsync(OwnerId, Sha256Lower(PdfBytes), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        Result<UploadDocumentResult> result = await CreateSut().HandleAsync(PdfCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsDuplicate.Should().BeTrue();
        result.Value.DuplicateOfDocumentId.Should().Be(existing.Id);
        result.Value.Document.Should().BeNull();

        // No orphan blob, no second metadata row, no job: dedupe creates nothing.
        _storage.VerifyNoOtherCalls();
        _jobQueue.VerifyNoOtherCalls();
        _documents.Verify(
            d => d.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenEnqueueFails_CompensatesAndReturnsUploadFailed()
    {
        Document? added = null;

        _documents
            .Setup(d => d.FindActiveByContentHashAsync(OwnerId, Sha256Lower(PdfBytes), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        _documents
            .Setup(d => d.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((doc, _) => added = doc)
            .Returns(Task.CompletedTask);
        _documents
            .Setup(d => d.RemoveAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storage
            .Setup(s => s.SaveAsync(It.IsAny<Stream>(), "application/pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorageKey);
        _storage
            .Setup(s => s.DeleteAsync(StorageKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _jobQueue
            .Setup(q => q.EnqueueAnalysisAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Guid>(Error.Unexpected("queue unavailable")));

        Result<UploadDocumentResult> result = await CreateSut().HandleAsync(PdfCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unexpected);
        result.Error.Code.Should().Be(DocumentsErrorCodes.UploadFailed);

        // Atomic from the client's perspective: the half-created upload is undone.
        _documents.Verify(d => d.RemoveAsync(added!, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(StorageKey, It.IsAny<CancellationToken>()), Times.Once);
    }
}
