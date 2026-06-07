using Filer.Modules.Documents.Domain;
using Filer.SharedKernel.Paging;

namespace Filer.Modules.Documents.Persistence;

/// <summary>
/// Persistence seam for the Documents slices, mirroring <c>IRefreshTokenStore</c>
/// (Auth) and <c>IAnalysisJobStore</c> (BackgroundJobs): feature services stay
/// unit-testable without a database, while the EF implementation is exercised
/// against real Postgres in Filer.IntegrationTests (12-testing-strategy.md — no
/// EF in-memory, don't mock what you own).
/// </summary>
public interface IDocumentStore
{
    /// <summary>
    /// The duplicate-detection lookup: the caller's non-deleted document with the
    /// given content hash, or null (02-data-model.md, Duplicate Detection).
    /// </summary>
    Task<Document?> FindActiveByContentHashAsync(Guid ownerId, string contentHash, CancellationToken cancellationToken);

    /// <summary>
    /// The caller's non-deleted document with the given id, or null. Owner-scoped
    /// by construction so cross-owner and missing are indistinguishable — the
    /// uniform-404 rule's single chokepoint (05-security.md).
    /// </summary>
    Task<Document?> FindActiveByIdAsync(Guid ownerId, Guid documentId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the caller's non-deleted documents matching the filter,
    /// newest first (03-api-specification.md, List filters). Owner-scoped by
    /// construction, like every read on this seam (05-security.md).
    /// </summary>
    Task<PagedResult<Document>> ListActiveAsync(DocumentListFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the caller owns a folder with the given id — the move-target check
    /// for the update slice, owner-scoped like every read on this seam so a
    /// missing and a cross-owner folder are indistinguishable (05-security.md).
    /// </summary>
    Task<bool> OwnedFolderExistsAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);

    /// <summary>Persists a new document immediately.</summary>
    Task AddAsync(Document document, CancellationToken cancellationToken);

    /// <summary>Persists changes to an already-loaded document immediately.</summary>
    Task UpdateAsync(Document document, CancellationToken cancellationToken);

    /// <summary>
    /// Hard-removes a row — compensation for a just-created document whose upload
    /// could not complete, never user-facing deletion (which is soft, 02).
    /// </summary>
    Task RemoveAsync(Document document, CancellationToken cancellationToken);
}
