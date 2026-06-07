using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Storage.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.DownloadContent;

/// <summary>
/// The download slice (03-api-specification.md): resolve the caller's document,
/// then open the blob via <c>IFileStorageProvider</c> (07). Cross-owner, missing,
/// and soft-deleted documents are indistinguishable to the caller — all 404, never
/// 403, so document ids cannot be probed (05-security.md).
/// </summary>
public sealed class DownloadDocumentContentService(
    IDocumentStore documents,
    IFileStorageProvider storage,
    ICurrentUser currentUser,
    ILogger<DownloadDocumentContentService> logger)
{
    public async Task<Result<DownloadDocumentContentResult>> HandleAsync(
        Guid documentId, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filter below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<DownloadDocumentContentResult>(Error.Unauthorized());
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md).
        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<DownloadDocumentContentResult>(NotFound());
        }

        // A missing blob for existing metadata is an integrity violation, not a
        // business outcome: StorageBlobNotFoundException propagates by design and
        // the host's exception handler answers 500 (13, Result vs exceptions).
        Stream content = await storage.OpenReadAsync(document.StorageKey, cancellationToken);

        logger.ContentDownloaded(document.Id, currentUser.Id, document.SizeBytes);

        return Result.Success(new DownloadDocumentContentResult(
            content,
            document.ContentType,
            document.FileName,
            document.SizeBytes));
    }

    private static Error NotFound() =>
        Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound);
}

/// <summary>
/// Log messages for <see cref="DownloadDocumentContentService"/>, co-located per
/// the house pattern: compile-time-generated and allocation-free via
/// <c>[LoggerMessage]</c>. Ids and sizes only — never file names or content
/// (05-security.md).
/// </summary>
internal static partial class DownloadDocumentContentServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Document {DocumentId} content streamed to owner {OwnerId} ({SizeBytes} bytes).")]
    public static partial void ContentDownloaded(
        this ILogger logger, Guid documentId, Guid ownerId, long sizeBytes);
}
