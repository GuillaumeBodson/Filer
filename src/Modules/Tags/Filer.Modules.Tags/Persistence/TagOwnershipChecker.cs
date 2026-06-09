using Filer.Modules.Tags.Contracts;

namespace Filer.Modules.Tags.Persistence;

/// <summary>
/// The module's implementation of the cross-module ownership contract: a thin
/// adapter over <see cref="ITagStore"/> so the owner-scoped lookup stays in one
/// place and consumers never see the store seam. Mirrors
/// <c>FolderOwnershipChecker</c> (ADR-009). An empty set is true by definition;
/// otherwise every distinct id must resolve to an owned tag — the store counts
/// only the caller's rows, so a count short of the distinct id total means at
/// least one id is missing or cross-owner, which the caller maps to a uniform 404
/// (05-security.md).
/// </summary>
internal sealed class TagOwnershipChecker(ITagStore tags) : ITagOwnershipChecker
{
    public async Task<bool> OwnsAllTagsAsync(
        Guid ownerId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        // Distinct so a body that repeats an id is not counted twice against the
        // owned-row count; an empty set owns nothing-and-everything (vacuous true).
        Guid[] distinctIds = tagIds.Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return true;
        }

        int owned = await tags.CountOwnedAsync(ownerId, distinctIds, cancellationToken);

        return owned == distinctIds.Length;
    }
}
