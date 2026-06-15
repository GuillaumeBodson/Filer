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

    /// <summary>
    /// Whether the caller has a non-deleted document directly in the folder — the
    /// document half of the folder-emptiness check behind ADR-007's 409.
    /// Owner-scoped like every read on this seam (05-security.md).
    /// </summary>
    Task<bool> AnyActiveInFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);

    /// <summary>
    /// Stamps <c>DeletedAt</c> (and <c>UpdatedAt</c>) with the given timestamp on
    /// every non-deleted document of the caller inside the given folders, in one
    /// transaction (ADR-007: the cascade shares one timestamp). Returns the ids of
    /// the documents affected, so the caller can cancel their analysis jobs.
    /// </summary>
    Task<IReadOnlyList<Guid>> SoftDeleteActiveInFoldersAsync(
        Guid ownerId, IReadOnlyCollection<Guid> folderIds, DateTimeOffset deletedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every tag association of the document, in no particular order — the current
    /// state the replace/add slices diff against (#49, ADR-009). Not owner-scoped
    /// itself: the caller resolves the owned document first through
    /// <see cref="FindActiveByIdAsync"/>, so by the time this runs the document is
    /// already proven to be the caller's (05-security.md).
    /// </summary>
    Task<IReadOnlyList<DocumentTag>> ListTagsForDocumentAsync(
        Guid documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a computed set of association changes in one transaction (#49): rows
    /// in <paramref name="toInsert"/> are added, rows in <paramref name="toPromote"/>
    /// are existing pairs whose <c>Source</c> is updated, and rows in
    /// <paramref name="toRemove"/> are deleted. The slice owns the
    /// preserve-vs-promote decision and hands this method only the resulting diff —
    /// already split by operation, so persistence carries no business rule
    /// (13-code-quality-and-design.md).
    /// </summary>
    Task ApplyTagChangesAsync(
        IReadOnlyCollection<DocumentTag> toInsert,
        IReadOnlyCollection<DocumentTag> toPromote,
        IReadOnlyCollection<DocumentTag> toRemove,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes every <c>DocumentTag</c> row for the given tag whose document
    /// belongs to the caller, in one transaction — the persistence half of the
    /// tag-delete cascade (#48, ADR-009). Owner-scoping is transitive through the
    /// document, since the join carries no owner of its own (05-security.md): a
    /// cross-owner tag id matches none of the caller's documents and removes
    /// nothing. Idempotent: a tag with no associations is a no-op success, so the
    /// cascade is safe to retry.
    /// </summary>
    Task RemoveDocumentTagsForTagAsync(Guid ownerId, Guid tagId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the apply-suggestions outcome in one transaction (#55): the
    /// optional folder move (<paramref name="movedDocument"/>, null when the
    /// folder was not confirmed) and the new <c>AiSuggested</c> association rows
    /// commit together or not at all — one SaveChanges, like
    /// <see cref="ApplyTagChangesAsync"/>. The slice owns every decision; this
    /// method only writes the handed diff (13-code-quality-and-design.md).
    /// </summary>
    Task ApplyAnalysisAsync(
        Document? movedDocument,
        IReadOnlyCollection<DocumentTag> tagsToInsert,
        CancellationToken cancellationToken);

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
