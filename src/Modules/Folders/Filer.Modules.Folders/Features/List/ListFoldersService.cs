using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Folders.Features.List;

/// <summary>
/// The list-folders slice (03-api-specification.md): parse the requested view,
/// load the caller's active folders once, and shape them flat (default) or as the
/// nested hierarchy. Owner scoping is structural — the store cannot be queried
/// without the caller's id — and soft-deleted folders are excluded by the store
/// (05-security.md, 02-data-model.md). Tree assembly is pure in-memory work over
/// that single owner-scoped read, so both views cost one query.
/// </summary>
public sealed class ListFoldersService(
    IFolderStore folders,
    ICurrentUser currentUser,
    ILogger<ListFoldersService> logger)
{
    public async Task<Result<IReadOnlyList<FolderListItemResponse>>> HandleAsync(
        ListFoldersQuery query, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // owner-scoped read below must never run with an anonymous principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<IReadOnlyList<FolderListItemResponse>>(Error.Unauthorized());
        }

        Result<FolderListView> view = ListFoldersValidator.Validate(query);
        if (view.IsFailure)
        {
            return Result.Failure<IReadOnlyList<FolderListItemResponse>>(view.Error!);
        }

        IReadOnlyList<Folder> owned = await folders.ListActiveAsync(currentUser.Id, cancellationToken);

        IReadOnlyList<FolderListItemResponse> items = view.Value == FolderListView.Tree
            ? AssembleTree(owned)
            : owned.Select(f => FolderListItemResponse.From(f)).ToList();

        logger.FolderListServed(currentUser.Id, view.Value, owned.Count);

        return Result.Success(items);
    }

    /// <summary>
    /// Nests the owner's folders by <c>ParentId</c>, preserving the store's
    /// name-then-id order at every level (the input is ordered and the grouping is
    /// order-preserving). Roots are top-level folders — plus, defensively, any
    /// folder whose parent is absent from the active set: the delete slice
    /// (ADR-007) forbids that state, but if it ever occurs the folder surfaces at
    /// the root instead of silently vanishing from the tree.
    /// </summary>
    private static List<FolderListItemResponse> AssembleTree(IReadOnlyList<Folder> owned)
    {
        ILookup<Guid?, Folder> byParent = owned.ToLookup(f => f.ParentId);
        HashSet<Guid> activeIds = owned.Select(f => f.Id).ToHashSet();

        List<FolderListItemResponse> Build(IEnumerable<Folder> nodes) =>
            nodes
                .Select(f => FolderListItemResponse.From(f, Build(byParent[f.Id])))
                .ToList();

        IEnumerable<Folder> roots = owned.Where(
            f => f.ParentId is not Guid parentId || !activeIds.Contains(parentId));

        return Build(roots);
    }
}

/// <summary>
/// Log messages for <see cref="ListFoldersService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids and counts only — never folder names (05-security.md). Debug level:
/// listing is routine and high-frequency, like the Documents list.
/// </summary>
internal static partial class ListFoldersServiceLog
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Folder list ({View}) served to owner {OwnerId}: {Count} folders.")]
    public static partial void FolderListServed(
        this ILogger logger, Guid ownerId, FolderListView view, int count);
}
