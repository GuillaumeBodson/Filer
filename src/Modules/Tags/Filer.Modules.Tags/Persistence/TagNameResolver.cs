using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;

namespace Filer.Modules.Tags.Persistence;

/// <summary>
/// The module's implementation of the cross-module name-resolution contract: a
/// thin adapter over <see cref="ITagStore"/> so consumers never see the store
/// seam, mirroring <see cref="TagOwnershipChecker"/>. Matching happens in memory
/// over the owner's full tag list — bounded small per owner (a personal label
/// set), which keeps the case-insensitive comparison in one place instead of
/// negotiating collations with the database (13-code-quality-and-design.md, no
/// anticipation).
/// </summary>
internal sealed class TagNameResolver(ITagStore tags) : ITagNameResolver
{
    public async Task<IReadOnlyList<ResolvedTag>> ResolveOwnedByNamesAsync(
        Guid ownerId, IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        string[] wanted = names
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (wanted.Length == 0)
        {
            return [];
        }

        IReadOnlyList<Tag> owned = await tags.ListAsync(ownerId, cancellationToken);

        var resolved = new List<ResolvedTag>(wanted.Length);
        foreach (string name in wanted)
        {
            // Exact-case match wins over a case-insensitive one; ListAsync orders
            // by name then id, so ties resolve deterministically.
            Tag? match =
                owned.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal))
                ?? owned.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                resolved.Add(new ResolvedTag(match.Id, match.Name));
            }
        }

        return resolved;
    }
}
