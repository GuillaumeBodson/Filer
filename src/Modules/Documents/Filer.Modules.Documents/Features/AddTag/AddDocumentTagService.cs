using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.ReplaceTags;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Tags.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.AddTag;

/// <summary>
/// The add-tag slice (03-api-specification.md, ADR-009): associate a single tag
/// with the document as <c>User</c>. Upsert semantics — promote an existing
/// <c>AiSuggested</c> row, and idempotent when the pair is already a <c>User</c>
/// row (the composite key forbids a duplicate). The document and the tag must both
/// be the caller's, else a uniform 404 (05-security.md). Returns the document's
/// resulting tag set, reusing the replace slice's DTO.
/// </summary>
public sealed class AddDocumentTagService(
    IDocumentStore documents,
    ITagOwnershipChecker tagOwnership,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<AddDocumentTagService> logger)
{
    public async Task<Result<DocumentTagsResponse>> HandleAsync(
        Guid documentId, Guid tagId, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership checks below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<DocumentTagsResponse>(Error.Unauthorized());
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md).
        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<DocumentTagsResponse>(
                Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound));
        }

        // The tag must be the caller's; cross-owner and missing are indistinguishable
        // from absent, exactly like the document (05-security.md).
        if (!await tagOwnership.OwnsAllTagsAsync(currentUser.Id, [tagId], cancellationToken))
        {
            return Result.Failure<DocumentTagsResponse>(
                Error.NotFound("The tag was not found.", DocumentsErrorCodes.TagNotFound));
        }

        IReadOnlyList<DocumentTag> current = await documents.ListTagsForDocumentAsync(
            documentId, cancellationToken);
        DocumentTag? existing = current.FirstOrDefault(a => a.TagId == tagId);

        if (existing is null)
        {
            var inserted = new DocumentTag
            {
                DocumentId = documentId,
                TagId = tagId,
                Source = DocumentTagSource.User,
                CreatedAt = clock.UtcNow,
            };
            await documents.ApplyTagChangesAsync([inserted], [], [], cancellationToken);
            logger.TagAdded(documentId, currentUser.Id, tagId, promoted: false);
        }
        else if (existing.Source == DocumentTagSource.AiSuggested)
        {
            existing.Source = DocumentTagSource.User;
            await documents.ApplyTagChangesAsync([], [existing], [], cancellationToken);
            logger.TagAdded(documentId, currentUser.Id, tagId, promoted: true);
        }
        // Already a User row: idempotent no-op, no write.

        IReadOnlyList<DocumentTag> updated = await documents.ListTagsForDocumentAsync(
            documentId, cancellationToken);

        return Result.Success(DocumentTagsResponse.From(documentId, updated));
    }
}

/// <summary>
/// Log messages for <see cref="AddDocumentTagService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids only — never tag names (05-security.md).
/// </summary>
internal static partial class AddDocumentTagServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Tag {TagId} added to document {DocumentId} by owner {OwnerId} (promoted: {Promoted}).")]
    public static partial void TagAdded(
        this ILogger logger, Guid documentId, Guid ownerId, Guid tagId, bool promoted);
}
