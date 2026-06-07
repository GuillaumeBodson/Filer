using Filer.Modules.Documents.Contracts;
using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Folders.Features.Delete;

/// <summary>
/// The delete slice, semantics resolved by ADR-007: an empty folder soft-deletes;
/// a non-empty one (any non-deleted child folder or document) is a 409 unless the
/// caller opted into <c>?recursive=true</c>, which cascade soft-deletes the whole
/// subtree — descendant folders and the documents inside them — every row stamped
/// with the same <c>DeletedAt</c>. The cascade is soft only: provider bytes stay
/// untouched (07-storage-and-deployment.md). No silent reparenting, ever.
/// Cross-owner and missing folders are a uniform 404 (05-security.md), which also
/// makes a repeated delete a 404; ownership covers the whole subtree structurally,
/// because it is collected from an owner-scoped snapshot.
///
/// <para>
/// Ordering: documents first, then folders — each half is one transaction in its
/// own module. If the folder stamping fails after the documents committed, the
/// tree is still intact (now emptier) and a retry completes the delete; the
/// reverse order could leave active documents inside deleted folders. Same
/// tolerated-window stance as the direct document delete's delete-then-cancel.
/// </para>
/// </summary>
public sealed class DeleteFolderService(
    IFolderStore folders,
    IFolderDocumentRemover documents,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<DeleteFolderService> logger)
{
    public async Task<Result> HandleAsync(
        Guid folderId, bool recursive, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filters below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure(Error.Unauthorized());
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md).
        Folder? folder = await folders.FindActiveByIdAsync(
            currentUser.Id, folderId, cancellationToken);
        if (folder is null)
        {
            return Result.Failure(
                Error.NotFound("The folder was not found.", FoldersErrorCodes.FolderNotFound));
        }

        // The subtree comes from one owner-scoped snapshot, so ownership over every
        // descendant is structural — a foreign folder can never enter the set.
        IReadOnlyList<Folder> owned = await folders.ListActiveAsync(currentUser.Id, cancellationToken);
        List<Guid> subtree = CollectSubtree(folder.Id, owned);

        if (!recursive)
        {
            // ADR-007's safe default: any non-deleted child folder or document
            // rejects the delete — nothing disappears without an explicit opt-in.
            bool hasChildFolders = subtree.Count > 1;
            if (hasChildFolders
                || await documents.AnyActiveInFolderAsync(currentUser.Id, folder.Id, cancellationToken))
            {
                return Result.Failure(Error.Conflict(
                    "The folder is not empty. Retry with '?recursive=true' to delete the folder and everything in it.",
                    FoldersErrorCodes.NotEmpty));
            }
        }

        DateTimeOffset now = clock.UtcNow;

        // Documents first (see the type remarks for the ordering rationale). The
        // empty non-recursive case skips the call: the check above proved there is
        // nothing to remove.
        int documentsDeleted = 0;
        if (recursive)
        {
            Result<int> removed = await documents.SoftDeleteInFoldersAsync(
                currentUser.Id, subtree, now, cancellationToken);
            if (removed.IsFailure)
            {
                logger.CascadeDocumentRemovalFailed(folder.Id, currentUser.Id, removed.Error!.Code);

                return Result.Failure(removed.Error);
            }

            documentsDeleted = removed.Value;
        }

        int foldersDeleted = await folders.SoftDeleteAsync(
            currentUser.Id, subtree, now, cancellationToken);

        logger.FolderDeleted(folder.Id, currentUser.Id, recursive, foldersDeleted, documentsDeleted);

        return Result.Success();
    }

    /// <summary>
    /// The folder and every non-deleted descendant, breadth-first over the
    /// snapshot's ParentId edges — the same adjacency walk as the move slice's
    /// cycle check, in the other direction.
    /// </summary>
    private static List<Guid> CollectSubtree(Guid rootId, IReadOnlyList<Folder> owned)
    {
        ILookup<Guid?, Folder> byParent = owned.ToLookup(f => f.ParentId);

        var subtree = new List<Guid>();
        var frontier = new Queue<Guid>();
        frontier.Enqueue(rootId);

        while (frontier.TryDequeue(out Guid currentId))
        {
            subtree.Add(currentId);
            foreach (Folder child in byParent[currentId])
            {
                frontier.Enqueue(child.Id);
            }
        }

        return subtree;
    }
}

/// <summary>
/// Log messages for <see cref="DeleteFolderService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids, flags, and counts only — never folder names (05-security.md). Information
/// level: deletions are rare and audit-worthy, like the other mutations.
/// </summary>
internal static partial class DeleteFolderServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Folder {FolderId} soft-deleted by owner {OwnerId} (recursive: {Recursive}); {FolderCount} folder(s) and {DocumentCount} document(s) affected.")]
    public static partial void FolderDeleted(
        this ILogger logger, Guid folderId, Guid ownerId, bool recursive, int folderCount, int documentCount);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Folder {FolderId} cascade by owner {OwnerId} failed while removing documents ({ErrorCode}); no folder was deleted.")]
    public static partial void CascadeDocumentRemovalFailed(
        this ILogger logger, Guid folderId, Guid ownerId, string errorCode);
}
