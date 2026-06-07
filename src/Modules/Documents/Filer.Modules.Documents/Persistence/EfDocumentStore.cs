using Filer.Modules.Documents.Domain;
using Filer.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Documents.Persistence;

/// <summary>EF Core implementation of <see cref="IDocumentStore"/> over the module's context.</summary>
public sealed class EfDocumentStore(DocumentsDbContext db) : IDocumentStore
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
        // Tags arrive in M4 (#41–#45). Until the DocumentTag join exists no
        // document carries any tag, so a tag filter matches nothing by
        // definition — short-circuit instead of fabricating a join.
        if (filter.TagId is not null)
        {
            return new PagedResult<Document>([], filter.Page, filter.PageSize, 0);
        }

        IQueryable<Document> query = db.Documents
            .AsNoTracking()
            .Where(d => d.OwnerId == filter.OwnerId && d.DeletedAt == null);

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

    // Folders arrive in M4 (#40–#44). Until the folders table exists nobody owns
    // any folder, so the check fails by definition — short-circuit instead of
    // fabricating a join, exactly like the tag filter above. M4 replaces this
    // body with the real owner-scoped lookup behind the same seam.
    public Task<bool> OwnedFolderExistsAsync(
        Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

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
