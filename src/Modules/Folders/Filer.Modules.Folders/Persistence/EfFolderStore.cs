using Filer.Modules.Folders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Folders.Persistence;

/// <summary>EF Core implementation of <see cref="IFolderStore"/> over the module's context.</summary>
public sealed class EfFolderStore(FoldersDbContext db) : IFolderStore
{
    public Task<bool> ActiveExistsAsync(
        Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        db.Folders
            .AsNoTracking()
            .AnyAsync(
                f => f.Id == folderId && f.OwnerId == ownerId && f.DeletedAt == null,
                cancellationToken);

    public Task<bool> ActiveSiblingNameExistsAsync(
        Guid ownerId, Guid? parentId, string name, CancellationToken cancellationToken) =>
        db.Folders
            .AsNoTracking()
            .AnyAsync(
                f => f.OwnerId == ownerId && f.ParentId == parentId && f.Name == name && f.DeletedAt == null,
                cancellationToken);

    public async Task AddAsync(Folder folder, CancellationToken cancellationToken)
    {
        db.Folders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Folder>> ListActiveAsync(
        Guid ownerId, CancellationToken cancellationToken) =>
        await db.Folders
            .AsNoTracking()
            .Where(f => f.OwnerId == ownerId && f.DeletedAt == null)
            .OrderBy(f => f.Name)
            .ThenBy(f => f.Id)
            .ToListAsync(cancellationToken);

    public Task<Folder?> FindActiveByIdAsync(
        Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        db.Folders
            .AsNoTracking()
            .FirstOrDefaultAsync(
                f => f.Id == folderId && f.OwnerId == ownerId && f.DeletedAt == null,
                cancellationToken);
}
