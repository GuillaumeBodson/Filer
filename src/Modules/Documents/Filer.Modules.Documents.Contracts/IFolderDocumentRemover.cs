using Filer.SharedKernel.Results;

namespace Filer.Modules.Documents.Contracts;

/// <summary>
/// The cross-module seam the Folders module deletes documents through (ADR-007):
/// when a folder subtree is cascade soft-deleted, the documents inside it go with
/// it — soft only, bytes untouched (07-storage-and-deployment.md) — and their
/// queued/running analysis jobs are cancelled exactly like a direct document
/// delete (06-ai-analysis-pipeline.md). Same stance as
/// <c>IFolderOwnershipChecker</c> in the other direction: a narrow contract owned
/// by the module that owns the data (10-solution-structure.md).
/// </summary>
public interface IFolderDocumentRemover
{
    /// <summary>
    /// Whether the caller has any non-deleted document directly in the folder —
    /// the document half of ADR-007's emptiness check (the folder half lives in
    /// the Folders module). Owner-scoped like every cross-module read
    /// (05-security.md).
    /// </summary>
    Task<bool> AnyActiveInFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes every non-deleted document of the caller inside the given
    /// folders, stamping each with <paramref name="deletedAt"/> (ADR-007: the
    /// whole cascade shares one timestamp), then cancels their queued/running
    /// analysis jobs. The document stamping is one transaction; cancellation
    /// follows the delete-then-cancel ordering the direct document delete
    /// established. Returns the number of documents soft-deleted; folders with no
    /// active documents are a success with count zero.
    /// </summary>
    Task<Result<int>> SoftDeleteInFoldersAsync(
        Guid ownerId, IReadOnlyCollection<Guid> folderIds, DateTimeOffset deletedAt,
        CancellationToken cancellationToken);
}
