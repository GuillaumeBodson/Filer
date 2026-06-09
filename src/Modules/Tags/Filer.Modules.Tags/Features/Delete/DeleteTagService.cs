using Filer.Modules.Documents.Contracts;
using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Tags.Features.Delete;

/// <summary>
/// The delete-tag slice (03-api-specification.md): resolve the caller's tag, drop
/// its document-tag associations, then hard-delete the tag itself. Tags carry no
/// soft-delete state (Tag.cs), so the delete is real and irreversible; cross-owner
/// and missing tags are a uniform 404 (05-security.md), which also makes a repeated
/// delete a 404.
///
/// <para>
/// Cross-module mechanism (ADR-009): the join rows live in the Documents schema,
/// so they are removed through <see cref="IDocumentTagRemover"/> — the Documents
/// module's own seam — not reached into directly. Ordering mirrors the folder
/// cascade: associations FIRST, then the tag. If the tag delete fails after the
/// associations are gone, the tag still resolves and a retry completes the delete;
/// the reverse order could orphan association rows pointing at a tag that no
/// longer exists. The association removal is owner-scoped and idempotent, so the
/// retry is safe.
/// </para>
/// </summary>
public sealed class DeleteTagService(
    ITagStore tags,
    IDocumentTagRemover associations,
    ICurrentUser currentUser,
    ILogger<DeleteTagService> logger)
{
    public async Task<Result> HandleAsync(Guid tagId, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filters below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure(Error.Unauthorized());
        }

        // One owner-scoped lookup: anything it does not return is a uniform 404
        // (05-security.md).
        Tag? tag = await tags.FindByIdAsync(currentUser.Id, tagId, cancellationToken);
        if (tag is null)
        {
            return Result.Failure(
                Error.NotFound("The tag was not found.", TagsErrorCodes.TagNotFound));
        }

        // Associations first (see the type remarks for the ordering rationale):
        // owner-scoped through the Documents seam, so no foreign rows can be touched
        // and a retry after a later failure is harmless.
        await associations.RemoveAllForTagAsync(currentUser.Id, tag.Id, cancellationToken);

        await tags.DeleteAsync(tag, cancellationToken);

        logger.TagDeleted(tag.Id, currentUser.Id);

        return Result.Success();
    }
}

/// <summary>
/// Log messages for <see cref="DeleteTagService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids only — never tag names (05-security.md). Information level: deletions are
/// rare and audit-worthy, like the other mutations.
/// </summary>
internal static partial class DeleteTagServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Tag {TagId} deleted by owner {OwnerId}; its document associations were removed.")]
    public static partial void TagDeleted(this ILogger logger, Guid tagId, Guid ownerId);
}
