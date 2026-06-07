namespace Filer.Modules.Folders.Contracts;

/// <summary>
/// Cross-module ownership check: whether the caller owns an active (non-deleted)
/// folder. The Folders module implements it over its own schema; other modules
/// consume it through this contract only (10-solution-structure.md, ADR-004).
/// Owner-scoped by construction so cross-owner, missing, and soft-deleted folders
/// are indistinguishable — callers map <c>false</c> to the uniform 404
/// (05-security.md).
/// </summary>
public interface IFolderOwnershipChecker
{
    Task<bool> OwnsActiveFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);
}
