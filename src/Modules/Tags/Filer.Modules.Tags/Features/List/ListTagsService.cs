using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Tags.Features.List;

/// <summary>
/// The list-tags slice (03-api-specification.md): load the caller's tags once,
/// ordered by the store, and shape them to the wire DTO. Owner scoping is
/// structural — the store cannot be queried without the caller's id
/// (05-security.md). Tags are flat and per-owner, so unlike the Folders list
/// slice there is no view to parse and no hierarchy to assemble.
/// </summary>
public sealed class ListTagsService(
    ITagStore tags,
    ICurrentUser currentUser,
    ILogger<ListTagsService> logger)
{
    public async Task<Result<IReadOnlyList<TagListItemResponse>>> HandleAsync(
        CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // owner-scoped read below must never run with an anonymous principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<IReadOnlyList<TagListItemResponse>>(Error.Unauthorized());
        }

        IReadOnlyList<Tag> owned = await tags.ListAsync(currentUser.Id, cancellationToken);

        List<TagListItemResponse> items = owned.Select(TagListItemResponse.From).ToList();

        logger.TagListServed(currentUser.Id, items.Count);

        return Result.Success<IReadOnlyList<TagListItemResponse>>(items);
    }
}

/// <summary>
/// Log messages for <see cref="ListTagsService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids and counts only — never tag names (05-security.md). Debug level: listing
/// is routine and high-frequency, like the Folders and Documents lists.
/// </summary>
internal static partial class ListTagsServiceLog
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Tag list served to owner {OwnerId}: {Count} tags.")]
    public static partial void TagListServed(this ILogger logger, Guid ownerId, int count);
}
