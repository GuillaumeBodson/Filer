using Filer.Modules.Documents.Contracts;
using Filer.Modules.Search.Contracts;
using Filer.Modules.Search.Features.SearchDocuments;
using Filer.Modules.Search.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Paging;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Search.Tests.Features.SearchDocuments;

/// <summary>
/// The search slice's success path and every <c>Error</c> path, at its designed
/// seam (12-testing-strategy.md): normalization into the owner-scoped
/// <see cref="DocumentSearchQuery"/>, the DTO mapping, and the envelope. The real
/// tsvector/ranking SQL is exercised against Postgres in Filer.IntegrationTests.
/// </summary>
public sealed class SearchDocumentsServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly Mock<IOwnerDocumentSearch> _search = new(MockBehavior.Strict);

    private SearchDocumentsService CreateSut(ICurrentUser? caller = null) =>
        new(
            _search.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<SearchDocumentsService>.Instance);

    [Fact]
    public async Task HandleAsync_WithDefaults_QueriesPageOneOfTwentyScopedToTheCaller()
    {
        DocumentSearchQuery? captured = null;
        _search
            .Setup(s => s.SearchAsync(It.IsAny<DocumentSearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback((DocumentSearchQuery query, CancellationToken _) => captured = query)
            .ReturnsAsync(EmptyPage(1, 20));

        Result<PagedResult<SearchHitResponse>> result =
            await CreateSut().HandleAsync(new SearchDocumentsQuery("facture", null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().Be(new DocumentSearchQuery(OwnerId, "facture", 1, 20));
    }

    [Fact]
    public async Task HandleAsync_TrimsTheTermAndPassesPagingThrough()
    {
        DocumentSearchQuery? captured = null;
        _search
            .Setup(s => s.SearchAsync(It.IsAny<DocumentSearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback((DocumentSearchQuery query, CancellationToken _) => captured = query)
            .ReturnsAsync(EmptyPage(3, 50));

        await CreateSut().HandleAsync(new SearchDocumentsQuery("  facture 2024  ", 3, 50), CancellationToken.None);

        captured.Should().Be(new DocumentSearchQuery(OwnerId, "facture 2024", 3, 50));
    }

    [Fact]
    public async Task HandleAsync_MapsEveryDtoFieldAndTheEnvelopeFromTheContractPage()
    {
        var documentId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 7, 2, 11, 30, 0, TimeSpan.Zero);
        var hit = new DocumentSearchHit(
            documentId, folderId, "facture_2024.pdf", "application/pdf", 1234,
            "Ready", createdAt, updatedAt, 0.42);
        _search
            .Setup(s => s.SearchAsync(It.IsAny<DocumentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<DocumentSearchHit>([hit], 2, 1, 3));

        Result<PagedResult<SearchHitResponse>> result =
            await CreateSut().HandleAsync(new SearchDocumentsQuery("facture", 2, 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        PagedResult<SearchHitResponse> page = result.Value;
        page.Page.Should().Be(2);
        page.PageSize.Should().Be(1);
        page.TotalCount.Should().Be(3);
        page.Items.Should().ContainSingle().Which.Should().Be(new SearchHitResponse(
            documentId,
            folderId,
            "facture_2024.pdf",
            "application/pdf",
            1234,
            "Ready",
            createdAt,
            updatedAt,
            0.42));
    }

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheContract()
    {
        Result<PagedResult<SearchHitResponse>> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(new SearchDocumentsQuery("facture", null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _search.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, null, null, SearchErrorCodes.SearchTermInvalid)]
    [InlineData("   ", null, null, SearchErrorCodes.SearchTermInvalid)]
    [InlineData("facture", 0, null, SearchErrorCodes.PageInvalid)]
    [InlineData("facture", null, 0, SearchErrorCodes.PageSizeInvalid)]
    [InlineData("facture", null, SearchDocumentsValidator.MaxPageSize + 1, SearchErrorCodes.PageSizeInvalid)]
    public async Task HandleAsync_WithAnInvalidRequest_ReturnsValidationWithoutTouchingTheContract(
        string? term, int? page, int? pageSize, string expectedCode)
    {
        Result<PagedResult<SearchHitResponse>> result =
            await CreateSut().HandleAsync(new SearchDocumentsQuery(term, page, pageSize), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(expectedCode);

        _search.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WithAnOverlongTerm_ReturnsValidationWithoutTouchingTheContract()
    {
        string term = new('a', SearchDocumentsValidator.MaxSearchTermLength + 1);

        Result<PagedResult<SearchHitResponse>> result =
            await CreateSut().HandleAsync(new SearchDocumentsQuery(term, null, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(SearchErrorCodes.SearchTermInvalid);

        _search.VerifyNoOtherCalls();
    }

    private static PagedResult<DocumentSearchHit> EmptyPage(int page, int pageSize) =>
        new([], page, pageSize, 0);
}
