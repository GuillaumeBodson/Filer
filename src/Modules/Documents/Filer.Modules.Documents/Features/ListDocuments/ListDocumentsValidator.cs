using Filer.Modules.Documents.Contracts;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Documents.Features.ListDocuments;

/// <summary>
/// Structural validation of the list request — explicit, dependency-free checks
/// in the slice (13-code-quality-and-design.md). Bounds live here as constants:
/// 04-non-functional.md defines no paging limits, so they are slice decisions,
/// not configuration.
/// </summary>
internal static class ListDocumentsValidator
{
    internal const int DefaultPage = 1;

    internal const int DefaultPageSize = 20;

    internal const int MaxPageSize = 100;

    /// <summary>Matches the FileName column bound — a longer term can never match anything.</summary>
    internal const int MaxSearchTermLength = 255;

    public static Result Validate(ListDocumentsQuery query)
    {
        if (query.Page is < 1)
        {
            return Result.Failure(Error.Validation(
                "The page must be 1 or greater.",
                DocumentsErrorCodes.PageInvalid));
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            return Result.Failure(Error.Validation(
                $"The page size must be between 1 and {MaxPageSize}.",
                DocumentsErrorCodes.PageSizeInvalid));
        }

        if (query.SearchTerm is { Length: > MaxSearchTermLength })
        {
            return Result.Failure(Error.Validation(
                $"The search term must not exceed {MaxSearchTermLength} characters.",
                DocumentsErrorCodes.SearchTermInvalid));
        }

        return Result.Success();
    }
}
