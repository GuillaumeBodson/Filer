using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.GetMetadata;

/// <summary>
/// The get-metadata slice (03-api-specification.md): resolve the caller's
/// document and map it to the response DTO. Cross-owner, missing, and
/// soft-deleted documents are indistinguishable to the caller — all 404, never
/// 403, so document ids cannot be probed (05-security.md).
/// </summary>
public sealed class GetDocumentMetadataService(
    IDocumentStore documents,
    ICurrentUser currentUser,
    ILogger<GetDocumentMetadataService> logger)
{
    public async Task<Result<DocumentMetadataResponse>> HandleAsync(
        Guid documentId, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filter below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<DocumentMetadataResponse>(Error.Unauthorized());
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md).
        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<DocumentMetadataResponse>(
                Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound));
        }

        logger.MetadataServed(document.Id, currentUser.Id);

        return Result.Success(new DocumentMetadataResponse(
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
/// Log messages for <see cref="GetDocumentMetadataService"/>, co-located per the
/// house pattern: compile-time-generated and allocation-free via
/// <c>[LoggerMessage]</c>. Ids only — never file names or content (05-security.md).
/// Debug level: metadata reads are routine and high-frequency, unlike content
/// downloads.
/// </summary>
internal static partial class GetDocumentMetadataServiceLog
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Document {DocumentId} metadata served to owner {OwnerId}.")]
    public static partial void MetadataServed(this ILogger logger, Guid documentId, Guid ownerId);
}
