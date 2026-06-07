using Filer.Modules.Folders.Domain;

namespace Filer.Modules.Folders.Persistence;

/// <summary>
/// Persistence seam for the Folders slices, mirroring <c>IDocumentStore</c>
/// (Documents): feature services stay unit-testable without a database, while the
/// EF implementation is exercised against real Postgres in Filer.IntegrationTests
/// (12-testing-strategy.md — no EF in-memory, don't mock what you own).
/// </summary>
public interface IFolderStore
{
    /// <summary>
    /// Whether the caller has a non-deleted folder with the given id. Owner-scoped
    /// by construction so cross-owner and missing are indistinguishable — the
    /// uniform-404 rule's single chokepoint (05-security.md).
    /// </summary>
    Task<bool> ActiveExistsAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the caller already has a non-deleted folder named <paramref name="name"/>
    /// under <paramref name="parentId"/> (null = top level) — the sibling-uniqueness
    /// pre-check behind the 409 (02-data-model.md).
    /// </summary>
    Task<bool> ActiveSiblingNameExistsAsync(
        Guid ownerId, Guid? parentId, string name, CancellationToken cancellationToken);

    Task AddAsync(Folder folder, CancellationToken cancellationToken);

    /// <summary>
    /// Every non-deleted folder the caller owns, ordered by name then id so the
    /// listing is deterministic (03-api-specification.md). Owner-scoped by
    /// construction and soft-deleted rows excluded (05-security.md, 02-data-model.md).
    /// </summary>
    Task<IReadOnlyList<Folder>> ListActiveAsync(Guid ownerId, CancellationToken cancellationToken);
}
