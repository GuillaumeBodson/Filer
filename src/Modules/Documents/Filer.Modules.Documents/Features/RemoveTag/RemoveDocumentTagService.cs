using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.RemoveTag;

/// <summary>
/// The remove-tag slice (03-api-specification.md, ADR-009): drop a single
/// association regardless of its <c>Source</c> — this is the only way an
/// <c>AiSuggested</c> row is removed. The document must be the caller's, else a
/// uniform 404 (05-security.md). A pair that is not associated is also a 404: the
/// remove has nothing to act on, mirroring the document-delete stance where a
/// missing target is not found rather than a silent success.
/// </summary>
public sealed class RemoveDocumentTagService(
    IDocumentStore documents,
    ICurrentUser currentUser,
    ILogger<RemoveDocumentTagService> logger)
{
    public async Task<Result> HandleAsync(
        Guid documentId, Guid tagId, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filter below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure(Error.Unauthorized());
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md). Resolving the owned document first
        // means no tag-ownership check is needed — the association can only exist
        // on the caller's own document.
        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure(
                Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound));
        }

        IReadOnlyList<DocumentTag> current = await documents.ListTagsForDocumentAsync(
            documentId, cancellationToken);
        DocumentTag? association = current.FirstOrDefault(a => a.TagId == tagId);
        if (association is null)
        {
            return Result.Failure(
                Error.NotFound("The tag association was not found.", DocumentsErrorCodes.TagNotFound));
        }

        await documents.ApplyTagChangesAsync([], [], [association], cancellationToken);

        logger.TagRemoved(documentId, currentUser.Id, tagId, association.Source);

        return Result.Success();
    }
}

/// <summary>
/// Log messages for <see cref="RemoveDocumentTagService"/>, co-located per the
/// house pattern: compile-time-generated and allocation-free via
/// <c>[LoggerMessage]</c>. Ids and the source only — never tag names (05-security.md).
/// </summary>
internal static partial class RemoveDocumentTagServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Tag {TagId} ({Source}) removed from document {DocumentId} by owner {OwnerId}.")]
    public static partial void TagRemoved(
        this ILogger logger, Guid documentId, Guid ownerId, Guid tagId, DocumentTagSource source);
}
