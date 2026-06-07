using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Features.ListDocuments;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.ListDocuments;

/// <summary>
/// The list request's structural rules, exercised at their boundaries
/// (12-testing-strategy.md): omitted parameters are valid (defaults are the
/// service's concern), in-range extremes pass, out-of-range values fail with
/// their stable error code.
/// </summary>
public sealed class ListDocumentsValidatorTests
{
    [Fact]
    public void Validate_WithEveryParameterOmitted_Succeeds()
    {
        Result result = ListDocumentsValidator.Validate(
            new ListDocumentsQuery(null, null, null, null, null));

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, ListDocumentsValidator.MaxPageSize)]
    [InlineData(int.MaxValue, ListDocumentsValidator.DefaultPageSize)]
    public void Validate_WithInRangePaging_Succeeds(int page, int pageSize)
    {
        Result result = ListDocumentsValidator.Validate(
            new ListDocumentsQuery(null, null, null, page, pageSize));

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithPageBelowOne_FailsWithPageInvalid(int page)
    {
        Result result = ListDocumentsValidator.Validate(
            new ListDocumentsQuery(null, null, null, page, null));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.PageInvalid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ListDocumentsValidator.MaxPageSize + 1)]
    public void Validate_WithOutOfRangePageSize_FailsWithPageSizeInvalid(int pageSize)
    {
        Result result = ListDocumentsValidator.Validate(
            new ListDocumentsQuery(null, null, null, null, pageSize));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.PageSizeInvalid);
    }

    [Fact]
    public void Validate_WithSearchTermAtTheBound_Succeeds()
    {
        string searchTerm = new('a', ListDocumentsValidator.MaxSearchTermLength);

        Result result = ListDocumentsValidator.Validate(
            new ListDocumentsQuery(null, null, searchTerm, null, null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithSearchTermPastTheBound_FailsWithSearchTermInvalid()
    {
        string searchTerm = new('a', ListDocumentsValidator.MaxSearchTermLength + 1);

        Result result = ListDocumentsValidator.Validate(
            new ListDocumentsQuery(null, null, searchTerm, null, null));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.SearchTermInvalid);
    }
}
