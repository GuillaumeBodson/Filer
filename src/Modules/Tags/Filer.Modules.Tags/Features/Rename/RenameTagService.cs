using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Tags.Features.Rename;

/// <summary>
/// The rename-tag slice (03-api-specification.md): validate the request, resolve
/// the caller's tag, enforce per-owner name uniqueness, apply. Cross-owner and
/// missing tags are a uniform 404 (05-security.md) — tags are flat and hard
/// deleted (#48), so the lookup has no soft-delete state to consider, unlike the
/// Folders update slice. The name check is the business 409; the unique index on
/// (OwnerId, Name) is the race-condition backstop (02-data-model.md). Renaming a
/// tag to its current name is a no-op that succeeds rather than a self-conflict.
/// </summary>
public sealed class RenameTagService(
    ITagStore tags,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<RenameTagService> logger)
{
    public async Task<Result<RenameTagResponse>> HandleAsync(
        Guid tagId, RenameTagRequest request, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filters below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<RenameTagResponse>(Error.Unauthorized());
        }

        Result validation = RenameTagValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<RenameTagResponse>(validation.Error!);
        }

        // One owner-scoped lookup: anything it does not return is a uniform 404
        // (05-security.md).
        Tag? tag = await tags.FindByIdAsync(currentUser.Id, tagId, cancellationToken);
        if (tag is null)
        {
            return Result.Failure<RenameTagResponse>(
                Error.NotFound("The tag was not found.", TagsErrorCodes.NotFound));
        }

        // The validator guarantees a non-empty trimmed name; persist and compare
        // the trimmed form so "  urgent " and "urgent" are the same tag.
        string newName = request.Name!.Trim();

        // Only a changed name can collide: the current name would only ever match
        // the tag's own row, which is not a conflict — so renaming to the same
        // name succeeds.
        if (newName != tag.Name
            && await tags.NameExistsAsync(currentUser.Id, newName, cancellationToken))
        {
            return Result.Failure<RenameTagResponse>(Error.Conflict(
                "A tag with the same name already exists.",
                TagsErrorCodes.NameConflict));
        }

        tag.Name = newName;
        tag.UpdatedAt = clock.UtcNow;

        await tags.UpdateAsync(tag, cancellationToken);

        logger.TagRenamed(tag.Id, currentUser.Id);

        return Result.Success(RenameTagResponse.From(tag));
    }
}

/// <summary>
/// Log messages for <see cref="RenameTagService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids only — never tag names (05-security.md). Information level: mutations are
/// rare and audit-worthy, unlike reads.
/// </summary>
internal static partial class RenameTagServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Tag {TagId} renamed by owner {OwnerId}.")]
    public static partial void TagRenamed(this ILogger logger, Guid tagId, Guid ownerId);
}
