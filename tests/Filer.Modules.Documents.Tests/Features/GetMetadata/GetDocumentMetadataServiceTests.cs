using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.GetMetadata;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.GetMetadata;

/// <summary>
/// The get-metadata slice's success path and every <c>Error</c> path, at its
/// designed seams (12-testing-strategy.md). The owner-scoped EF lookup and the
/// real HTTP status mapping are exercised in Filer.IntegrationTests.
/// </summary>
public sealed class GetDocumentMetadataServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid FolderId = Guid.NewGuid();

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);

    private GetDocumentMetadataService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<GetDocumentMetadataService>.Instance);

    [Fact]
    public async Task HandleAsync_WithOwnedDocument_MapsEveryDtoFieldFromTheEntity()
    {
        var createdAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 6, 2, 11, 30, 0, TimeSpan.Zero);
        var document = new Document
        {
            Id = DocumentId,
            OwnerId = OwnerId,
            FolderId = FolderId,
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1234,
            StorageKey = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234",
            ContentHash = "1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff",
            Status = DocumentStatus.Uploaded,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };

        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        Result<DocumentMetadataResponse> result =
            await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new DocumentMetadataResponse(
            DocumentId,
            FolderId,
            "invoice.pdf",
            "application/pdf",
            1234,
            "1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff",
            "Uploaded",
            createdAt,
            updatedAt));
    }

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStore()
    {
        Result<DocumentMetadataResponse> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(DocumentId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenLookupReturnsNothing_ReturnsNotFound()
    {
        // One arrangement, three real-world causes — missing id, another owner's
        // document, soft-deleted document — because the owner-scoped store lookup
        // is the single chokepoint that makes them indistinguishable (05-security.md).
        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        Result<DocumentMetadataResponse> result =
            await CreateSut().HandleAsync(DocumentId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.DocumentNotFound);
    }
}
