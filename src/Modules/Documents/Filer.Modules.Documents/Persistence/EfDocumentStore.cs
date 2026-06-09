using Filer.Modules.Documents.Domain;
using Filer.Modules.Folders.Contracts;
using Filer.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Documents.Persistence;

/// <summary>EF Core implementation of <see cref="IDocumentStore"/> over the module's context.</summary>
public sealed class EfDocumentStore(DocumentsDbContext db, IFolderOwnershipChecker folderOwnership) : IDocumentStore
{
    public Task<Document?> FindActiveByContentHashAsync(
        Guid ownerId, string contentHash, CancellationToken cancellationToken) =>
        db.Documents
            .AsNoTracking()
            .Where(d => d.OwnerId == ownerId && d.ContentHash == contentHash && d.DeletedAt == null)
            .OrderBy(d => d.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Document?> FindActiveByIdAsync(
        Guid ownerId, Guid documentId, CancellationToken cancellationToken) =>
        db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.Id == documentId && d.OwnerId == ownerId && d.DeletedAt == null,
                cancellationToken);

    public async Task<PagedResult<Document>> ListActiveAsync(
        DocumentListFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<Document> query = db.Documents
            .AsNoTracking()
            .Where(d => d.OwnerId == filter.OwnerId && d.DeletedAt == null);

        // The DocumentTag join now exists (#49): a tag filter keeps documents that
        // carry the tag in any Source. The join is owner-scoped transitively — the
        // OwnerId predicate above already confines the documents — so a foreign
        // tag id simply matches none of the caller's documents (05-security.md),
        // no separate tag-ownership check needed for a read.
        if (filter.TagId is Guid tagId)
        {
            query = query.Where(d => db.DocumentTags.Any(dt => dt.DocumentId == d.Id && dt.TagId == tagId));
        }

        if (filter.FolderId is Guid folderId)
        {
            query = query.Where(d => d.FolderId == folderId);
        }

        if (filter.SearchTerm is { Length: > 0 } searchTerm)
        {
            // Case-insensitive contains on the file name (ILIKE), with LIKE
            // metacharacters escaped so user input is matched literally.
            // Upgraded to tsvector/GIN full-text in M6 (#56) behind this seam.
            string pattern = $"%{EscapeLikePattern(searchTerm)}%";
            query = query.Where(d => EF.Functions.ILike(d.FileName, pattern, "\\"));
        }

        long totalCount = await query.LongCountAsync(cancellationToken);

        // Newest first; the id tiebreaker keeps paging stable when documents
        // share a CreatedAt timestamp (bulk uploads).
        List<Document> items = await query
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Document>(items, filter.Page, filter.PageSize, totalCount);
    }

    /// <summary>Escapes <c>\</c>, <c>%</c> and <c>_</c> so a search term is a literal, not a pattern.</summary>
    private static string EscapeLikePattern(string term) =>
        term
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    // The Folders module owns folder data; the check crosses the module boundary
    // through its Contracts project only (10-solution-structure.md, ADR-004) —
    // same seam the M3 stub promised, now backed by the real owner-scoped,
    // soft-delete-aware lookup (#96).
    public Task<bool> OwnedFolderExistsAsync(
        Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        folderOwnership.OwnsActiveFolderAsync(ownerId, folderId, cancellationToken);

    public Task<bool> AnyActiveInFolderAsync(
        Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        db.Documents
            .AsNoTracking()
            .AnyAsync(
                d => d.OwnerId == ownerId && d.FolderId == folderId && d.DeletedAt == null,
                cancellationToken);

    public async Task<IReadOnlyList<Guid>> SoftDeleteActiveInFoldersAsync(
        Guid ownerId, IReadOnlyCollection<Guid> folderIds, DateTimeOffset deletedAt,
        CancellationToken cancellationToken)
    {
        // One tracked load + one SaveChanges = one transaction for the whole
        // document half of the cascade (ADR-007). Owner-scoped and
        // soft-delete-aware like every read on this seam (05-security.md), so a
        // cross-owner folder id in the set could never touch foreign rows.
        List<Document> documents = await db.Documents
            .Where(d => d.OwnerId == ownerId
                && d.FolderId != null && folderIds.Contains(d.FolderId.Value)
                && d.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (Document document in documents)
        {
            document.DeletedAt = deletedAt;
            document.UpdatedAt = deletedAt;
        }

        await db.SaveChangesAsync(cancellationToken);

        return documents.Select(d => d.Id).ToList();
    }

    public async Task<IReadOnlyList<DocumentTag>> ListTagsForDocumentAsync(
        Guid documentId, CancellationToken cancellationToken) =>
        await db.DocumentTags
            .AsNoTracking()
            .Where(dt => dt.DocumentId == documentId)
            .ToListAsync(cancellationToken);

    public async Task ApplyTagChangesAsync(
        IReadOnlyCollection<DocumentTag> toInsert,
        IReadOnlyCollection<DocumentTag> toPromote,
        IReadOnlyCollection<DocumentTag> toRemove,
        CancellationToken cancellationToken)
    {
        // Reads on this seam are no-tracking, so attach explicitly by operation.
        // The slice has already split the diff, so there is no guessing here:
        // inserts are new pairs, promotes are existing pairs with a changed Source,
        // removes are deletions. One SaveChanges = one transaction for the whole
        // diff (#49).
        foreach (DocumentTag association in toRemove)
        {
            db.DocumentTags.Remove(association);
        }

        foreach (DocumentTag association in toPromote)
        {
            db.DocumentTags.Update(association);
        }

        if (toInsert.Count > 0)
        {
            db.DocumentTags.AddRange(toInsert);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(Document document, CancellationToken cancellationToken)
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Document document, CancellationToken cancellationToken)
    {
        // Reads on this seam are no-tracking, so reattach explicitly. Marking the
        // whole entity modified trades a minimal UPDATE for simplicity — fine at
        // this row size (13-code-quality-and-design.md, no anticipation).
        db.Documents.Update(document);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Document document, CancellationToken cancellationToken)
    {
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
    }
}
