using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.UpdateMetadata;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.UpdateMetadata;

/// <summary>
/// The update-metadata slice's success paths and every <c>Error</c> path, at its
/// designed seams (12-testing-strategy.md). The owner-scoped EF lookup, the real
/// folder check, and the HTTP status mapping are exercised in
/// Filer.IntegrationTests.
/// </summary>
public sealed class UpdateDocumentMetadataServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid TargetFolderId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);

    private UpdateDocumentMetadataService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<UpdateDocumentMetadataService>.Instance);

    private static Document OwnedDocument() => new()
    {
        Id = DocumentId,
        OwnerId = OwnerId,
        FolderId = null,
        FileName = "original.pdf",
        ContentType = "application/pdf",
        SizeBytes = 1234,
        StorageKey = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234",
        ContentHash = "1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff",
        Status = DocumentStatus.Uploaded,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1),
    };

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStore()
    {
        var request = new UpdateDocumentMetadataRequest { FileName = "renamed.pdf" };

        Result<UpdateDocumentMetadataResponse> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(DocumentId, request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenValidationFails_PropagatesTheErrorWithoutTouchingTheStore()
    {
        // One representative failure; every validation rule is pinned in
        // UpdateDocumentMetadataValidatorTests.
        var request = new UpdateDocumentMetadataRequest();

        Result<UpdateDocumentMetadataResponse> result =
            await CreateSut().HandleAsync(DocumentId, request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.UpdateEmpty);

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

        var request = new UpdateDocumentMetadataRequest { FileName = "renamed.pdf" };

        Result<UpdateDocumentMetadataResponse> result =
            await CreateSut().HandleAsync(DocumentId, request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.DocumentNotFound);
    }

    [Fact]
    public async Task HandleAsync_WhenTargetFolderIsNotOwned_ReturnsNotFoundWithoutSaving()
    {
        // Same uniform-404 stance as documents: a cross-owner folder and a missing
        // folder are indistinguishable (05-security.md).
        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnedDocument());
        _documents
            .Setup(d => d.OwnedFolderExistsAsync(OwnerId, TargetFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new UpdateDocumentMetadataRequest { FolderId = TargetFolderId };

        Result<UpdateDocumentMetadataResponse> result =
            await CreateSut().HandleAsync(DocumentId, request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.FolderNotFound);

        _documents.Verify(
            d => d.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Rename_PersistsTheNewNameAndStampsUpdatedAt()
    {
        Document document = OwnedDocument();
        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        _documents
            .Setup(d => d.UpdateAsync(document, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateDocumentMetadataRequest { FileName = "renamed.pdf" };

        Result<UpdateDocumentMetadataResponse> result =
            await CreateSut().HandleAsync(DocumentId, request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        document.FileName.Should().Be("renamed.pdf");
        document.FolderId.Should().BeNull("an absent folderId leaves the folder untouched");
        document.UpdatedAt.Should().Be(Now);

        result.Value!.FileName.Should().Be("renamed.pdf");
        result.Value.UpdatedAt.Should().Be(Now);

        _documents.Verify(d => d.UpdateAsync(document, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_MoveToOwnedFolder_PersistsTheNewFolder()
    {
        Document document = OwnedDocument();
        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        _documents
            .Setup(d => d.OwnedFolderExistsAsync(OwnerId, TargetFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _documents
            .Setup(d => d.UpdateAsync(document, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateDocumentMetadataRequest { FolderId = TargetFolderId };

        Result<UpdateDocumentMetadataResponse> result =
            await CreateSut().HandleAsync(DocumentId, request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        document.FolderId.Should().Be(TargetFolderId);
        document.FileName.Should().Be("original.pdf", "an absent fileName leaves the name untouched");
        result.Value!.FolderId.Should().Be(TargetFolderId);
    }

    [Fact]
    public async Task HandleAsync_MoveToRoot_PersistsWithoutAnyFolderCheck()
    {
        // The strict mock is the assertion: an explicit-null target must not hit
        // OwnedFolderExistsAsync — the root always exists and is nobody's to own.
        Document document = OwnedDocument();
        document.FolderId = TargetFolderId;

        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        _documents
            .Setup(d => d.UpdateAsync(document, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateDocumentMetadataRequest { FolderId = null };

        Result<UpdateDocumentMetadataResponse> result =
            await CreateSut().HandleAsync(DocumentId, request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        document.FolderId.Should().BeNull();
        result.Value!.FolderId.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_RenameAndMoveTogether_MapsEveryDtoFieldFromTheEntity()
    {
        Document document = OwnedDocument();
        _documents
            .Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        _documents
            .Setup(d => d.OwnedFolderExistsAsync(OwnerId, TargetFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _documents
            .Setup(d => d.UpdateAsync(document, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateDocumentMetadataRequest
        {
            FileName = "renamed.pdf",
            FolderId = TargetFolderId,
        };

        Result<UpdateDocumentMetadataResponse> result =
            await CreateSut().HandleAsync(DocumentId, request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new UpdateDocumentMetadataResponse(
            DocumentId,
            TargetFolderId,
            "renamed.pdf",
            "application/pdf",
            1234,
            "1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff",
            "Uploaded",
            document.CreatedAt,
            Now));
    }
}
