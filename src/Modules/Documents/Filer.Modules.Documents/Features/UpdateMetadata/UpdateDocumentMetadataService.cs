using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.UpdateMetadata;

/// <summary>
/// The update-metadata slice (03-api-specification.md): validate the patch,
/// resolve the caller's document, verify the move target, apply the changes.
/// Cross-owner, missing, and soft-deleted documents are a uniform 404
/// (05-security.md) — and so is a move target the caller does not own, for the
/// same reason: folder ids must not be probeable either.
/// </summary>
public sealed class UpdateDocumentMetadataService(
    IDocumentStore documents,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<UpdateDocumentMetadataService> logger)
{
    public async Task<Result<UpdateDocumentMetadataResponse>> HandleAsync(
        Guid documentId, UpdateDocumentMetadataRequest request, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filters below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<UpdateDocumentMetadataResponse>(Error.Unauthorized());
        }

        Result validation = UpdateDocumentMetadataValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<UpdateDocumentMetadataResponse>(validation.Error!);
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md).
        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<UpdateDocumentMetadataResponse>(
                Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound));
        }

        // A non-null target must be a folder the caller owns; explicit null is the
        // root and needs no check (02-data-model.md). Owner-scoped like the
        // document lookup, so cross-owner and missing folders are indistinguishable.
        if (request is { HasFolderId: true, FolderId: Guid targetFolderId }
            && !await documents.OwnedFolderExistsAsync(currentUser.Id, targetFolderId, cancellationToken))
        {
            return Result.Failure<UpdateDocumentMetadataResponse>(
                Error.NotFound("The target folder was not found.", DocumentsErrorCodes.FolderNotFound));
        }

        if (request.HasFileName)
        {
            document.FileName = request.FileName!;
        }

        if (request.HasFolderId)
        {
            document.FolderId = request.FolderId;
        }

        document.UpdatedAt = clock.UtcNow;

        await documents.UpdateAsync(document, cancellationToken);

        logger.MetadataUpdated(document.Id, currentUser.Id, request.HasFileName, request.HasFolderId);

        return Result.Success(new UpdateDocumentMetadataResponse(
            document.Id,
            document.FolderId,
            document.FileName,
            document.ContentType,
            document.SizeBytes,
            document.ContentHash,
            document.Status.ToString(),
            document.CreatedAt,
            document.UpdatedAt));
    }
}

/// <summary>
/// Log messages for <see cref="UpdateDocumentMetadataService"/>, co-located per
/// the house pattern: compile-time-generated and allocation-free via
/// <c>[LoggerMessage]</c>. Ids and flags only — never file names (05-security.md).
/// Information level: metadata mutations are rare and audit-worthy, unlike reads.
/// </summary>
internal static partial class UpdateDocumentMetadataServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Document {DocumentId} metadata updated by owner {OwnerId} (rename: {Renamed}, move: {Moved}).")]
    public static partial void MetadataUpdated(
        this ILogger logger, Guid documentId, Guid ownerId, bool renamed, bool moved);
}
