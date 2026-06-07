using System.Security.Cryptography;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Files;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Storage.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filer.Modules.Documents.Features.Upload;

/// <summary>
/// The upload slice (03-api-specification.md, upload behavior): validate → sniff →
/// hash → dedupe → persist bytes → persist metadata → queue analysis. The request
/// never performs AI work inline (06-ai-analysis-pipeline.md); it returns as soon
/// as the metadata is durable and the job is queued.
/// </summary>
public sealed class UploadDocumentService(
    IDocumentStore documents,
    IFileStorageProvider storage,
    IBackgroundJobQueue jobQueue,
    ICurrentUser currentUser,
    IOptions<DocumentsOptions> options,
    IClock clock,
    ILogger<UploadDocumentService> logger)
{
    public async Task<Result<UploadDocumentResult>> HandleAsync(
        UploadDocumentCommand command, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // owner stamp below must never come from an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<UploadDocumentResult>(Error.Unauthorized());
        }

        Result validation = UploadDocumentValidator.Validate(command, options.Value);
        if (validation.IsFailure)
        {
            return Result.Failure<UploadDocumentResult>(validation.Error!);
        }

        string mediaType = UploadDocumentValidator.NormalizeMediaType(command.ContentType);

        if (!await SniffsAsDeclaredAsync(command.Content, mediaType, cancellationToken))
        {
            return Result.Failure<UploadDocumentResult>(Error.UnsupportedMediaType(
                $"The file content does not match the declared content type '{mediaType}'.",
                DocumentsErrorCodes.ContentTypeMismatch));
        }

        // SHA-256 over the full content drives duplicate detection (02-data-model.md).
        command.Content.Position = 0;
        byte[] hashBytes = await SHA256.HashDataAsync(command.Content, cancellationToken);
        string contentHash = Convert.ToHexStringLower(hashBytes);

        // Dedupe before persisting bytes: a duplicate creates nothing — no orphan
        // blob to clean up. 409 with the existing reference; the client decides
        // whether to proceed (03-api-specification.md).
        Document? existing = await documents.FindActiveByContentHashAsync(
            currentUser.Id, contentHash, cancellationToken);
        if (existing is not null)
        {
            logger.UploadDeduplicated(currentUser.Id, existing.Id);

            return Result.Success(UploadDocumentResult.Duplicate(existing.Id));
        }

        command.Content.Position = 0;
        string storageKey = await storage.SaveAsync(command.Content, mediaType, cancellationToken);

        DateTimeOffset now = clock.UtcNow;
        var document = new Document
        {
            OwnerId = currentUser.Id,
            TenantId = currentUser.TenantId,
            FileName = command.FileName,
            ContentType = mediaType,
            SizeBytes = command.SizeBytes,
            StorageKey = storageKey,
            ContentHash = contentHash,
            Status = DocumentStatus.Uploaded,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await documents.AddAsync(document, cancellationToken);

        Result<Guid> enqueued = await jobQueue.EnqueueAnalysisAsync(document.Id, cancellationToken);
        if (enqueued.IsFailure)
        {
            logger.EnqueueFailed(document.Id, enqueued.Error!.Code);
            await RollBackUploadAsync(document, storageKey);

            return Result.Failure<UploadDocumentResult>(Error.Unexpected(
                "The upload could not be completed.",
                DocumentsErrorCodes.UploadFailed));
        }

        logger.DocumentUploaded(document.Id, document.OwnerId, document.SizeBytes, enqueued.Value);

        return Result.Success(UploadDocumentResult.Created(new UploadDocumentResponse(
            document.Id,
            document.FileName,
            document.ContentType,
            document.SizeBytes,
            document.ContentHash,
            document.Status.ToString(),
            document.CreatedAt,
            enqueued.Value)));
    }

    /// <summary>
    /// Declared MIME alone is client-controlled; corroborate it against the file's
    /// magic bytes before trusting it (05-security.md).
    /// </summary>
    private static async Task<bool> SniffsAsDeclaredAsync(
        Stream content, string mediaType, CancellationToken cancellationToken)
    {
        byte[] sample = new byte[FileSignatures.SampleLength];
        int sampled = await content.ReadAtLeastAsync(
            sample, FileSignatures.SampleLength, throwOnEndOfStream: false, cancellationToken);

        return FileSignatures.Matches(mediaType, sample.AsSpan(0, sampled));
    }

    /// <summary>
    /// The upload contract is atomic from the client's perspective: either a
    /// document with a queued job, or nothing. Compensation runs without the
    /// request token so a cancelled call still cleans up after itself.
    /// </summary>
    private async Task RollBackUploadAsync(Document document, string storageKey)
    {
        await documents.RemoveAsync(document, CancellationToken.None);
        await storage.DeleteAsync(storageKey, CancellationToken.None);
    }
}

/// <summary>
/// Log messages for <see cref="UploadDocumentService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids and sizes only — never file names or content (05-security.md).
/// </summary>
internal static partial class UploadDocumentServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Upload deduplicated for owner {OwnerId}: content matches document {DocumentId}.")]
    public static partial void UploadDeduplicated(this ILogger logger, Guid ownerId, Guid documentId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Enqueueing analysis for document {DocumentId} failed ({ErrorCode}); rolling back the upload.")]
    public static partial void EnqueueFailed(this ILogger logger, Guid documentId, string errorCode);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Document {DocumentId} uploaded by owner {OwnerId} ({SizeBytes} bytes); analysis job {JobId} queued.")]
    public static partial void DocumentUploaded(
        this ILogger logger, Guid documentId, Guid ownerId, long sizeBytes, Guid jobId);
}
