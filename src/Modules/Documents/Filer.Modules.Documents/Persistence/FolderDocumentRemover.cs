using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Contracts;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Persistence;

/// <summary>
/// The module's implementation of the folder-cascade contract (ADR-007): a thin
/// adapter over <see cref="IDocumentStore"/> and the job queue, mirroring
/// <c>FolderOwnershipChecker</c> in the other direction. Stamping is one
/// transaction in the store; job cancellation follows it, document by document —
/// the same delete-then-cancel ordering as the direct delete slice, with the same
/// tolerance: if a cancellation fails after the deletes are durable, the
/// documents are still gone for the user and an orphaned job is harmless, but the
/// failure surfaces instead of being swallowed (13-code-quality-and-design.md).
/// </summary>
internal sealed class FolderDocumentRemover(
    IDocumentStore documents,
    IBackgroundJobQueue jobQueue,
    ILogger<FolderDocumentRemover> logger) : IFolderDocumentRemover
{
    public Task<bool> AnyActiveInFolderAsync(
        Guid ownerId, Guid folderId, CancellationToken cancellationToken) =>
        documents.AnyActiveInFolderAsync(ownerId, folderId, cancellationToken);

    public async Task<Result<int>> SoftDeleteInFoldersAsync(
        Guid ownerId, IReadOnlyCollection<Guid> folderIds, DateTimeOffset deletedAt,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> deletedIds = await documents.SoftDeleteActiveInFoldersAsync(
            ownerId, folderIds, deletedAt, cancellationToken);

        foreach (Guid documentId in deletedIds)
        {
            Result<int> cancelled = await jobQueue.CancelForDocumentAsync(documentId, cancellationToken);
            if (cancelled.IsFailure)
            {
                logger.CascadeJobCancellationFailed(documentId, cancelled.Error!.Code);

                return Result.Failure<int>(cancelled.Error);
            }
        }

        return Result.Success(deletedIds.Count);
    }
}

/// <summary>
/// Log messages for <see cref="FolderDocumentRemover"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids and codes only — never file names (05-security.md).
/// </summary>
internal static partial class FolderDocumentRemoverLog
{
    [LoggerMessage(Level = LogLevel.Error,
        Message = "Document {DocumentId} was soft-deleted by a folder cascade but cancelling its analysis jobs failed ({ErrorCode}).")]
    public static partial void CascadeJobCancellationFailed(
        this ILogger logger, Guid documentId, string errorCode);
}
