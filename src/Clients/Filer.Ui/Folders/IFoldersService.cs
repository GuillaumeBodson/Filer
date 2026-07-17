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

    /// <summary>Creates a folder under <paramref name="parentId"/> (null = root).</summary>
    Task<FolderCreateResult> CreateAsync(string name, Guid? parentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames and/or re-parents a folder (merge-patch, 03-api-specification.md);
    /// moving with a null target goes to the root. A move that would create a cycle
    /// is rejected by the server (<c>folder_move_cycle</c>, 02-data-model.md).
    /// </summary>
    Task<FolderUpdateResult> UpdateAsync(Guid folderId, FolderUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a folder. Non-empty folders are rejected unless
    /// <paramref name="recursive"/> opts into the cascade (ADR-007).
    /// </summary>
    Task<ProblemDetailsView?> DeleteAsync(Guid folderId, bool recursive, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a folder list: exactly one side is set.</summary>
public sealed record FoldersListResult(
    IReadOnlyList<FolderListItemResponse>? Folders,
    ProblemDetailsView? Problem);

/// <summary>Outcome of a create: exactly one side is set.</summary>
public sealed record FolderCreateResult(
    CreateFolderResponse? Folder,
    ProblemDetailsView? Problem);

/// <summary>What to change on a folder; leave a member unset to keep it.</summary>
public sealed record FolderUpdate
{
    /// <summary>New name, or <c>null</c> to keep the current one.</summary>
    public string? NewName { get; init; }

    /// <summary>Whether the update re-parents at all.</summary>
    public bool MoveParent { get; init; }

    /// <summary>Target parent when <see cref="MoveParent"/> is set; <c>null</c> = root.</summary>
    public Guid? TargetParentId { get; init; }
}

/// <summary>Outcome of an update: exactly one side is set.</summary>
public sealed record FolderUpdateResult(
    UpdateFolderResponse? Folder,
    ProblemDetailsView? Problem);
