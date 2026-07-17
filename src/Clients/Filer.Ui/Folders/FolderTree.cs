using Filer.ApiClient.Generated.Models;

namespace Filer.Ui.Folders;

/// <summary>One folder in the assembled tree (children sorted by name).</summary>
public sealed record FolderNode(Guid Id, string Name, Guid? ParentId, IReadOnlyList<FolderNode> Children);

/// <summary>
/// Client-side tree assembly over the flat folder list (#138). Pure - the API
/// returns parent references, the UI derives the hierarchy.
/// </summary>
public static class FolderTree
{
    /// <summary>
    /// Builds the root-level nodes. A folder whose parent is not in the list (should
    /// not happen server-side) surfaces at the root rather than disappearing.
    /// </summary>
    public static IReadOnlyList<FolderNode> Build(IEnumerable<FolderListItemResponse> folders)
    {
        List<FolderListItemResponse> all = [.. folders.Where(f => f.Id is not null)];
        HashSet<Guid> known = [.. all.Select(f => f.Id!.Value)];

        ILookup<Guid?, FolderListItemResponse> byParent = all.ToLookup(
            f => f.ParentId is Guid parent && known.Contains(parent) ? parent : (Guid?)null);

        return BuildLevel(null, byParent);
    }

    /// <summary>
    /// The ids of every folder under <paramref name="folderId"/> (excluding itself) -
    /// the client-side half of cycle safety: a folder can't move into its own subtree,
    /// so those targets are never offered. The server remains the authority.
    /// </summary>
    public static IReadOnlySet<Guid> DescendantIds(IEnumerable<FolderListItemResponse> folders, Guid folderId)
    {
        ILookup<Guid?, Guid> childrenOf = folders
            .Where(f => f.Id is not null)
            .ToLookup(f => f.ParentId, f => f.Id!.Value);

        var descendants = new HashSet<Guid>();
        var queue = new Queue<Guid>([folderId]);
        while (queue.Count > 0)
        {
            foreach (Guid child in childrenOf[queue.Dequeue()])
            {
                if (descendants.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return descendants;
    }

    private static List<FolderNode> BuildLevel(
        Guid? parentId, ILookup<Guid?, FolderListItemResponse> byParent) =>
        [.. byParent[parentId]
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new FolderNode(
                f.Id!.Value,
                f.Name ?? "",
                f.ParentId,
                BuildLevel(f.Id, byParent)))];
}
