using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.DownloadContent;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.Modules.Storage.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.DownloadContent;

/// <summary>
/// The download slice's success path and every <c>Error</c> path, at its designed
/// seams (12-testing-strategy.md). The owner-scoped EF lookup and the real HTTP
/// status mapping are exercised in Filer.IntegrationTests.
/// </summary>
public sealed class DownloadDocumentContentServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();

    private const string StorageKey = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234";

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);
    private readonly Mock<IFileStorageProvider> _storage = new(MockBehavior.Strict);

    private DownloadDocumentContentService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            _storage.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<DownloadDocumentContentService>.Instance);

    [Fact]
    public async Task HandleAsync_WithOwnedDocument_ReturnsContentStreamAndMetadata()
    {
        var document = new Document
        {
            Id = DocumentId,
            OwnerId = OwnerId,
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1234,
            StorageKey = StorageKey,
        };
        using var blob = new MemoryStream([1, 2, 3]);

        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        _storage
            .Setup(s => s.OpenReadAsync(StorageKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blob);

        Result<DownloadDocumentContentResult> result =
            await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().BeSameAs(blob);
        result.Value.ContentType.Should().Be("application/pdf");
        result.Value.FileName.Should().Be("invoice.pdf");
        result.Value.SizeBytes.Should().Be(1234);
    }

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingAnySeam()
    {
        Result<DownloadDocumentContentResult> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(DocumentId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _documents.VerifyNoOtherCalls();
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenLookupReturnsNothing_ReturnsNotFoundWithoutOpeningStorage()
    {
        // One arrangement, three real-world causes — missing id, another owner's
        // document, soft-deleted document — because the owner-scoped store lookup
        // is the single chokepoint that makes them indistinguishable (05-security.md).
        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        Result<DownloadDocumentContentResult> result =
            await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.DocumentNotFound);

        // No blob access for a request that resolved to nothing.
        _storage.VerifyNoOtherCalls();
    }
}
