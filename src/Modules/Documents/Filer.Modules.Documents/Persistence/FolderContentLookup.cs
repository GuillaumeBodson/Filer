using Filer.Modules.Documents.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Documents.Persistence;

/// <summary>
/// The module's implementation of the folder-sample read (#119), directly over the
/// module-owned context. Owner-scoping happens in the WHERE clause itself: the
/// query matches documents by owner <i>and</i> folder, so a foreign folder id can
/// only ever match rows the caller owns — none — and returns empty
/// (05-security.md). Newest-first with an id tie-break keeps the sample
/// deterministic (12-testing-strategy.md).
/// </summary>
internal sealed class FolderContentLookup(DocumentsDbContext db) : IFolderContentLookup
{
    public async Task<IReadOnlyList<string>> GetFolderSampleAsync(
        Guid ownerId, Guid folderId, int take, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        return await db.Documents
            .AsNoTracking()
            .Where(d => d.OwnerId == ownerId && d.FolderId == folderId && d.DeletedAt == null)
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Take(take)
            .Select(d => d.FileName)
            .ToListAsync(cancellationToken);
    }
}
