using Filer.Modules.Tags.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Tags.Persistence;

/// <summary>EF Core implementation of <see cref="ITagStore"/> over the module's context.</summary>
public sealed class EfTagStore(TagsDbContext db) : ITagStore
{
    public Task<bool> NameExistsAsync(
        Guid ownerId, string name, CancellationToken cancellationToken) =>
        db.Tags
            .AsNoTracking()
            .AnyAsync(t => t.OwnerId == ownerId && t.Name == name, cancellationToken);

    public Task<int> CountOwnedAsync(
        Guid ownerId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken) =>
        db.Tags
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId && tagIds.Contains(t.Id))
            .CountAsync(cancellationToken);

    public async Task AddAsync(Tag tag, CancellationToken cancellationToken)
    {
        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<Tag?> FindByIdAsync(
        Guid ownerId, Guid tagId, CancellationToken cancellationToken) =>
        db.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tagId && t.OwnerId == ownerId, cancellationToken);

    public async Task UpdateAsync(Tag tag, CancellationToken cancellationToken)
    {
        // Reads on this seam are no-tracking, so reattach explicitly. Marking the
        // whole entity modified trades a minimal UPDATE for simplicity — fine at
        // this row size (13-code-quality-and-design.md, no anticipation).
        db.Tags.Update(tag);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Tag tag, CancellationToken cancellationToken)
    {
        // Reads on this seam are no-tracking, so attach explicitly before removing.
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> ListAsync(
        Guid ownerId, CancellationToken cancellationToken) =>
        await db.Tags
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.Name)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);
}
