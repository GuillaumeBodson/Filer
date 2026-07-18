using Filer.Modules.Search.Contracts;
using Filer.Modules.Search.Features.SearchDocuments;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Search.Tests.Features.SearchDocuments;

/// <summary>
/// Every validation rule of the search slice rejects and accepts as specified
/// (12-testing-strategy.md): the required term, its length bound, and the paging
/// bounds shared with the Documents list.
/// </summary>
public sealed class SearchDocumentsValidatorTests
{
    [Fact]
    public void Validate_WithATermAndDefaults_Succeeds()
    {
        Result result = SearchDocumentsValidator.Validate(new SearchDocumentsQuery("facture", null, null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsTheBoundaryValues()
    {
        string term = new('a', SearchDocumentsValidator.MaxSearchTermLength);
        var query = new SearchDocumentsQuery(term, 1, SearchDocumentsValidator.MaxPageSize);

        SearchDocumentsValidator.Validate(query).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithAMissingOrBlankTerm_FailsWithSearchTermInvalid(string? term)
    {
        Result result = SearchDocumentsValidator.Validate(new SearchDocumentsQuery(term, null, null));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(SearchErrorCodes.SearchTermInvalid);
    }

    [Fact]
    public void Validate_WithAnOverlongTerm_FailsWithSearchTermInvalid()
    {
        string term = new('a', SearchDocumentsValidator.MaxSearchTermLength + 1);

        Result result = SearchDocumentsValidator.Validate(new SearchDocumentsQuery(term, null, null));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(SearchErrorCodes.SearchTermInvalid);
    }

    [Fact]
    public void Validate_WithAPageBelowOne_FailsWithPageInvalid()
    {
        Result result = SearchDocumentsValidator.Validate(new SearchDocumentsQuery("facture", 0, null));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(SearchErrorCodes.PageInvalid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SearchDocumentsValidator.MaxPageSize + 1)]
    public void Validate_WithAnOutOfRangePageSize_FailsWithPageSizeInvalid(int pageSize)
    {
        Result result = SearchDocumentsValidator.Validate(new SearchDocumentsQuery("facture", null, pageSize));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(SearchErrorCodes.PageSizeInvalid);
    }
}
