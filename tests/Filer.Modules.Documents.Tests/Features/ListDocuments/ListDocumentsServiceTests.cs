using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.ListDocuments;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Documents.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Paging;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.ListDocuments;

/// <summary>
/// The list slice's success path and every <c>Error</c> path, at its designed
/// seams (12-testing-strategy.md): normalization into the owner-scoped filter,
/// the DTO mapping, and the envelope. The real filter/paging SQL is exercised
/// against Postgres in Filer.IntegrationTests.
/// </summary>
public sealed class ListDocumentsServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly Mock<IDocumentStore> _documents = new(MockBehavior.Strict);

    private ListDocumentsService CreateSut(ICurrentUser? caller = null) =>
        new(
            _documents.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<ListDocumentsService>.Instance);

    private static ListDocumentsQuery EmptyQuery => new(null, null, null, null, null);

    [Fact]
    public async Task HandleAsync_WithDefaults_QueriesPageOneOfTwentyScopedToTheCaller()
    {
        DocumentListFilter? captured = null;
        _documents
            .Setup(d => d.ListActiveAsync(It.IsAny<DocumentListFilter>(), It.IsAny<CancellationToken>()))
            .Callback((DocumentListFilter filter, CancellationToken _) => captured = filter)
            .ReturnsAsync(EmptyPage(1, 20));

        Result<PagedResult<DocumentListItemResponse>> result =
            await CreateSut().HandleAsync(EmptyQuery, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().Be(new DocumentListFilter(OwnerId, null, null, null, 1, 20));
    }

    [Fact]
    public async Task HandleAsync_PassesFiltersThroughAndTrimsTheSearchTerm()
    {
        var folderId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        DocumentListFilter? captured = null;
        _documents
            .Setup(d => d.ListActiveAsync(It.IsAny<DocumentListFilter>(), It.IsAny<CancellationToken>()))
            .Callback((DocumentListFilter filter, CancellationToken _) => captured = filter)
            .ReturnsAsync(EmptyPage(3, 50));

        var query = new ListDocumentsQuery(folderId, tagId, "  invoice  ", 3, 50);
        Result<PagedResult<DocumentListItemResponse>> result =
            await CreateSut().HandleAsync(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().Be(new DocumentListFilter(OwnerId, folderId, tagId, "invoice", 3, 50));
    }

    [Fact]
    public async Task HandleAsync_TreatsABlankSearchTermAsNoFilter()
    {
        DocumentListFilter? captured = null;
        _documents
            .Setup(d => d.ListActiveAsync(It.IsAny<DocumentListFilter>(), It.IsAny<CancellationToken>()))
            .Callback((DocumentListFilter filter, CancellationToken _) => captured = filter)
            .ReturnsAsync(EmptyPage(1, 20));

        var query = new ListDocumentsQuery(null, null, "   ", null, null);
        await CreateSut().HandleAsync(query, CancellationToken.None);

        captured!.SearchTerm.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_MapsEveryDtoFieldAndTheEnvelopeFromTheStorePage()
    {
        var documentId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 6, 2, 11, 30, 0, TimeSpan.Zero);
        var document = new Document
        {
            Id = documentId,
            OwnerId = OwnerId,
            FolderId = folderId,
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
            .Setup(d => d.ListActiveAsync(It.IsAny<DocumentListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Document>([document], 2, 1, 3));

        Result<PagedResult<DocumentListItemResponse>> result =
            await CreateSut().HandleAsync(new ListDocumentsQuery(null, null, null, 2, 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        PagedResult<DocumentListItemResponse> page = result.Value;
        page.Page.Should().Be(2);
        page.PageSize.Should().Be(1);
        page.TotalCount.Should().Be(3);
        page.Items.Should().ContainSingle().Which.Should().Be(new DocumentListItemResponse(
            documentId,
            folderId,
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
        Result<PagedResult<DocumentListItemResponse>> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(EmptyQuery, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _documents.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, null, DocumentsErrorCodes.PageInvalid)]
    [InlineData(null, 0, DocumentsErrorCodes.PageSizeInvalid)]
    [InlineData(null, ListDocumentsValidator.MaxPageSize + 1, DocumentsErrorCodes.PageSizeInvalid)]
    public async Task HandleAsync_WithInvalidPaging_ReturnsValidationWithoutTouchingTheStore(
        int? page, int? pageSize, string expectedCode)
    {
        var query = new ListDocumentsQuery(null, null, null, page, pageSize);

        Result<PagedResult<DocumentListItemResponse>> result =
            await CreateSut().HandleAsync(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(expectedCode);

        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WithAnOverlongSearchTerm_ReturnsValidationWithoutTouchingTheStore()
    {
        string searchTerm = new('a', ListDocumentsValidator.MaxSearchTermLength + 1);
        var query = new ListDocumentsQuery(null, null, searchTerm, null, null);

        Result<PagedResult<DocumentListItemResponse>> result =
            await CreateSut().HandleAsync(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.SearchTermInvalid);

        _documents.VerifyNoOtherCalls();
    }

    private static PagedResult<Document> EmptyPage(int page, int pageSize) =>
        new([], page, pageSize, 0);
}
