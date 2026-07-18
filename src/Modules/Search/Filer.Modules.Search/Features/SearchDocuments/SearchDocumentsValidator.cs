using Filer.Modules.Search.Contracts;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Search.Features.SearchDocuments;

/// <summary>
/// Structural validation of the search request — explicit, dependency-free
/// checks in the slice (13-code-quality-and-design.md). Paging bounds mirror the
/// Documents list slice so the two owner-facing collections page identically.
/// </summary>
internal static class SearchDocumentsValidator
{
    internal const int DefaultPage = 1;

    internal const int DefaultPageSize = 20;

    internal const int MaxPageSize = 100;

    /// <summary>
    /// Matches the FileName column bound (and the Documents list limit) — a
    /// longer term can never match a file name, and metadata values get no
    /// longer a query either.
    /// </summary>
    internal const int MaxSearchTermLength = 255;

    public static Result Validate(SearchDocumentsQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            return Result.Failure(Error.Validation(
                "The q search term is required.",
                SearchErrorCodes.SearchTermInvalid));
        }

        if (query.SearchTerm.Length > MaxSearchTermLength)
        {
            return Result.Failure(Error.Validation(
                $"The search term must not exceed {MaxSearchTermLength} characters.",
                SearchErrorCodes.SearchTermInvalid));
        }

        if (query.Page is < 1)
        {
            return Result.Failure(Error.Validation(
                "The page must be 1 or greater.",
                SearchErrorCodes.PageInvalid));
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            return Result.Failure(Error.Validation(
                $"The page size must be between 1 and {MaxPageSize}.",
                SearchErrorCodes.PageSizeInvalid));
        }

        return Result.Success();
    }
}
