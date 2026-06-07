using Filer.Modules.Documents.Domain;
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

    public async Task AddAsync(Document document, CancellationToken cancellationToken)
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Document document, CancellationToken cancellationToken)
    {
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
    }
}
