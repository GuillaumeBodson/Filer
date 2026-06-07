using Filer.Modules.Folders.Contracts;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Folders.Features.List;

/// <summary>
/// Structural validation of the list request — explicit, dependency-free checks
/// in the slice (13-code-quality-and-design.md). Validation and normalization are
/// one step here: the only rule is that <c>view</c> parses, so the result carries
/// the parsed <see cref="FolderListView"/> instead of making the service re-parse.
/// </summary>
internal static class ListFoldersValidator
{
    private const string FlatValue = "flat";

    private const string TreeValue = "tree";

    /// <summary>
    /// Absent or blank means the documented default (<c>flat</c>); the two known
    /// values parse case-insensitively (query strings are hand-typed); anything
    /// else is the spec's 400 (03-api-specification.md).
    /// </summary>
    public static Result<FolderListView> Validate(ListFoldersQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.View))
        {
            return Result.Success(FolderListView.Flat);
        }

        string view = query.View.Trim();

        if (view.Equals(FlatValue, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(FolderListView.Flat);
        }

        if (view.Equals(TreeValue, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(FolderListView.Tree);
        }

        return Result.Failure<FolderListView>(Error.Validation(
            $"The view must be '{FlatValue}' or '{TreeValue}'.",
            FoldersErrorCodes.ViewInvalid));
    }
}
