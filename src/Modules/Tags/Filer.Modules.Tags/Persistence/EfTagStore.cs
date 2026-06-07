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

    public async Task AddAsync(Tag tag, CancellationToken cancellationToken)
    {
        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);
    }
}
