using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Tags.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.ReplaceTags;

/// <summary>
/// The replace-tags slice (03-api-specification.md, ADR-009): set the document's
/// <c>User</c>-sourced tag set to exactly the supplied ids. AI suggestions are
/// preserved unless their tag is in the new set, in which case the existing row is
/// promoted to <c>User</c> — the composite key means one row per pair, so a
/// promotion is an update, never a duplicate. The document must be the caller's
/// and every supplied tag must be the caller's, else a uniform 404 (05-security.md):
/// a cross-owner or missing document or tag is indistinguishable from absent.
/// </summary>
public sealed class ReplaceDocumentTagsService(
    IDocumentStore documents,
    ITagOwnershipChecker tagOwnership,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<ReplaceDocumentTagsService> logger)
{
    public async Task<Result<DocumentTagsResponse>> HandleAsync(
        Guid documentId, ReplaceDocumentTagsRequest request, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership checks below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<DocumentTagsResponse>(Error.Unauthorized());
        }

        Result validation = ReplaceDocumentTagsValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<DocumentTagsResponse>(validation.Error!);
        }

        // Distinct so a repeated id is one target; the composite key collapses
        // duplicates anyway, and ownership is counted against distinct ids.
        IReadOnlyCollection<Guid> desiredTagIds = request.TagIds!.Distinct().ToArray();

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md).
        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<DocumentTagsResponse>(
                Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound));
        }

        // Every referenced tag must be the caller's. Cross-owner and missing tags
        // are indistinguishable from absent, exactly like the document (05-security.md).
        if (!await tagOwnership.OwnsAllTagsAsync(currentUser.Id, desiredTagIds, cancellationToken))
        {
            return Result.Failure<DocumentTagsResponse>(
                Error.NotFound("One or more tags were not found.", DocumentsErrorCodes.TagNotFound));
        }

        IReadOnlyList<DocumentTag> current = await documents.ListTagsForDocumentAsync(
            documentId, cancellationToken);

        DateTimeOffset now = clock.UtcNow;
        var desired = desiredTagIds.ToHashSet();

        var toInsert = new List<DocumentTag>();
        var toPromote = new List<DocumentTag>();
        var toRemove = new List<DocumentTag>();

        var existingByTag = current.ToDictionary(a => a.TagId);

        // For each desired tag: ensure a User row exists — insert if absent,
        // promote if an AiSuggested row exists, leave alone if already User.
        foreach (Guid tagId in desired)
        {
            if (!existingByTag.TryGetValue(tagId, out DocumentTag? existing))
            {
                toInsert.Add(new DocumentTag
                {
                    DocumentId = documentId,
                    TagId = tagId,
                    Source = DocumentTagSource.User,
                    CreatedAt = now,
                });
            }
            else if (existing.Source == DocumentTagSource.AiSuggested)
            {
                existing.Source = DocumentTagSource.User;
                toPromote.Add(existing);
            }
            // Already User and in the desired set: untouched.
        }

        // Existing User rows whose tag is no longer desired are removed; AiSuggested
        // rows not in the desired set are left untouched (ADR-009).
        foreach (DocumentTag existing in current)
        {
            if (existing.Source == DocumentTagSource.User && !desired.Contains(existing.TagId))
            {
                toRemove.Add(existing);
            }
        }

        await documents.ApplyTagChangesAsync(toInsert, toPromote, toRemove, cancellationToken);

        logger.TagsReplaced(documentId, currentUser.Id, desired.Count, toInsert.Count, toPromote.Count, toRemove.Count);

        IReadOnlyList<DocumentTag> updated = await documents.ListTagsForDocumentAsync(
            documentId, cancellationToken);

        return Result.Success(DocumentTagsResponse.From(documentId, updated));
    }
}

/// <summary>
/// Log messages for <see cref="ReplaceDocumentTagsService"/>, co-located per the
/// house pattern: compile-time-generated and allocation-free via
/// <c>[LoggerMessage]</c>. Ids and counts only — never tag names (05-security.md).
/// Information level: tag mutations are audit-worthy, like the metadata mutations.
/// </summary>
internal static partial class ReplaceDocumentTagsServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Document {DocumentId} user tags replaced by owner {OwnerId} (desired: {Desired}, inserted: {Inserted}, promoted: {Promoted}, removed: {Removed}).")]
    public static partial void TagsReplaced(
        this ILogger logger, Guid documentId, Guid ownerId,
        int desired, int inserted, int promoted, int removed);
}
