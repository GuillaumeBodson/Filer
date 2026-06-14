using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;

namespace Filer.Modules.Tags.Persistence;

/// <summary>
/// The module's implementation of the cross-module tag read: a thin adapter over
/// <see cref="ITagStore"/> (which already lists owner-scoped tags name-then-id
/// ordered), projecting to names only so the entity never crosses the boundary
/// (10-solution-structure.md). Mirrors <c>TagOwnershipChecker</c>.
/// </summary>
internal sealed class OwnerTagReader(ITagStore tags) : IOwnerTagReader
{
    public async Task<IReadOnlyList<string>> ListNamesAsync(
        Guid ownerId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Tag> owned = await tags.ListAsync(ownerId, cancellationToken);

        return [.. owned.Select(tag => tag.Name)];
    }
}
