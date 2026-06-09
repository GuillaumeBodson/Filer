using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.RemoveTag;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.RemoveTag;

/// <summary>
/// The remove-tag slice: removes any Source, and the Error paths, at its seams
/// (12-testing-strategy.md). No tag-ownership check is needed — the association can
/// only exist on the caller's already-resolved document.
/// </summary>
public sealed class RemoveDocumentTagServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid TagA = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);

    private RemoveDocumentTagService CreateSut(ICurrentUser? caller = null) =>
        new(_documents.Object, caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<RemoveDocumentTagService>.Instance);

    private static Document OwnedDocument() => new()
    {
        Id = DocumentId, OwnerId = OwnerId, FileName = "d.pdf", ContentType = "application/pdf",
        SizeBytes = 1, StorageKey = new string('a', 64), ContentHash = new string('b', 64),
        Status = DocumentStatus.Uploaded, CreatedAt = Now, UpdatedAt = Now,
    };

    private static DocumentTag Row(DocumentTagSource source) =>
        new() { DocumentId = DocumentId, TagId = TagA, Source = source, CreatedAt = Now };

    private void ArrangeOwnedDocument() =>
        _documents.Setup(d => d.FindActiveByIdAsync(OwnerId, DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnedDocument());

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
    public async Task HandleAsync_WhenAssociationAbsent_ReturnsNotFound()
    {
        ArrangeOwnedDocument();
        _documents.Setup(d => d.ListTagsForDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var result = await CreateSut().HandleAsync(DocumentId, TagA, CancellationToken.None);
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(DocumentsErrorCodes.TagNotFound);
    }

    [Theory]
    [InlineData(DocumentTagSource.User)]
    [InlineData(DocumentTagSource.AiSuggested)]
    public async Task HandleAsync_RemovesAssociation_RegardlessOfSource(DocumentTagSource source)
    {
        ArrangeOwnedDocument();
        var row = Row(source);
        _documents.Setup(d => d.ListTagsForDocumentAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([row]);
        _documents.Setup(d => d.ApplyTagChangesAsync(
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<IReadOnlyCollection<DocumentTag>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await CreateSut().HandleAsync(DocumentId, TagA, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _documents.Verify(d => d.ApplyTagChangesAsync(
            It.Is<IReadOnlyCollection<DocumentTag>>(i => i.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(p => p.Count == 0),
            It.Is<IReadOnlyCollection<DocumentTag>>(rm => rm.Count == 1 && rm.Single().TagId == TagA),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
