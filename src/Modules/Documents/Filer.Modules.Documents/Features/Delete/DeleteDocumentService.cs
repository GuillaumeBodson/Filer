using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.Delete;

/// <summary>
/// The delete slice (03-api-specification.md): resolve the caller's document,
/// stamp <c>DeletedAt</c>, then cancel its queued/running analysis jobs
/// (06-ai-analysis-pipeline.md). Deletion is soft — the row and the stored bytes
/// remain for the retention window (02-data-model.md, 04-non-functional.md).
/// Cross-owner, missing, and already-deleted documents are a uniform 404
/// (05-security.md), which also makes a repeated delete a 404, not an error.
/// The soft-delete commits before the cancellation: if the cleanup fails, the
/// document is still gone for the user, and an orphaned job is harmless — the
/// uniform-404 read seam already hides the document, and a worker finishing a
/// cancelled-too-late job records nothing.
/// </summary>
public sealed class DeleteDocumentService(
    IDocumentStore documents,
    IBackgroundJobQueue jobQueue,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<DeleteDocumentService> logger)
{
    public async Task<Result> HandleAsync(Guid documentId, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filter below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure(Error.Unauthorized());
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md).
        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure(
                Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound));
        }

        DateTimeOffset now = clock.UtcNow;
        document.DeletedAt = now;
        document.UpdatedAt = now;

        await documents.UpdateAsync(document, cancellationToken);

        // After the delete is durable, cancel the document's analysis work. The
        // only failure the queue contract defines is an empty document id, which a
        // resolved document rules out — but a failure must still surface, never be
        // swallowed (13-code-quality-and-design.md).
        Result<int> cancelled = await jobQueue.CancelForDocumentAsync(document.Id, cancellationToken);
        if (cancelled.IsFailure)
        {
            logger.JobCancellationFailed(document.Id, cancelled.Error!.Code);

            return Result.Failure(cancelled.Error);
        }

        logger.DocumentDeleted(document.Id, currentUser.Id, cancelled.Value);

        return Result.Success();
    }
}

/// <summary>
/// Log messages for <see cref="DeleteDocumentService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids and counts only — never file names (05-security.md). Information level:
/// deletions are rare and audit-worthy, like the other metadata mutations.
/// </summary>
internal static partial class DeleteDocumentServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Document {DocumentId} soft-deleted by owner {OwnerId}; {CancelledJobs} analysis job(s) cancelled.")]
    public static partial void DocumentDeleted(
        this ILogger logger, Guid documentId, Guid ownerId, int cancelledJobs);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Document {DocumentId} was soft-deleted but cancelling its analysis jobs failed ({ErrorCode}).")]
    public static partial void JobCancellationFailed(this ILogger logger, Guid documentId, string errorCode);
}
