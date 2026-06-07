using Filer.Modules.Folders.Contracts;

namespace Filer.Modules.Folders.Persistence;

/// <summary>
/// The module's implementation of the cross-module ownership contract: a thin
/// adapter over <see cref="IFolderStore"/> so the owner-scoped, soft-delete-aware
/// lookup stays in one place and consumers never see the store seam.
/// </summary>
internal sealed class FolderOwnershipChecker(IFolderStore folders) : IFolderOwnershipChecker
{
    public Task<bool> OwnsActiveFolderAsync(
        Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        folders.ActiveExistsAsync(ownerId, folderId, cancellationToken);
}
