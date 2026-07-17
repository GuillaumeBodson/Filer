using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;

namespace Filer.Ui.Folders;

/// <summary>
/// Folder access for the UI: the move-target picker today (#137), the full folder
/// tree with #138. Calls go through the typed Kiota client (ADR-011).
/// </summary>
public interface IFoldersService
{
    Task<FoldersListResult> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a folder list: exactly one side is set.</summary>
public sealed record FoldersListResult(
    IReadOnlyList<FolderListItemResponse>? Folders,
    ProblemDetailsView? Problem);
