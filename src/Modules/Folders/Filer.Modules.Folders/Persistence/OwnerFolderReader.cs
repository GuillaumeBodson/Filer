using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;

namespace Filer.Modules.Folders.Persistence;

/// <summary>
/// The module's implementation of the cross-module folder read: a thin adapter
/// over <see cref="IFolderStore"/> (which already lists owner-scoped, non-deleted
/// folders name-then-id ordered), projecting to the contract's minimal shape so
/// the entity never crosses the boundary (10-solution-structure.md). Mirrors
/// <c>FolderOwnershipChecker</c>.
/// </summary>
internal sealed class OwnerFolderReader(IFolderStore folders) : IOwnerFolderReader
{
    public async Task<IReadOnlyList<OwnerFolder>> ListActiveAsync(
        Guid ownerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Folder> active = await folders.ListActiveAsync(ownerId, cancellationToken);

        return [.. active.Select(folder => new OwnerFolder(folder.Id, folder.Name))];
    }
}
