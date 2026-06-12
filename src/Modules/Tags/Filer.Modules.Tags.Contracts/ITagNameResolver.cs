namespace Filer.Modules.Tags.Contracts;

/// <summary>
/// Cross-module name → tag resolution: the Documents apply-suggestions slice
/// (#55) confirms AI tag suggestions by NAME (06-ai-analysis-pipeline.md) and
/// needs the caller's matching tag ids to create the association rows. The Tags
/// module implements it over its own schema; other modules consume it through
/// this contract only (10-solution-structure.md, ADR-004). Owner-scoped by
/// construction, mirroring <see cref="ITagOwnershipChecker"/> (05-security.md).
/// </summary>
public interface ITagNameResolver
{
    /// <summary>
    /// The caller's tags whose name matches one of <paramref name="names"/>
    /// (trimmed, case-insensitive). Names with no owned tag are simply absent
    /// from the result — the caller decides what an unresolved name means. When
    /// several owned tags match one name case-insensitively, the exact-case match
    /// wins, otherwise the first by name ordering, so resolution is deterministic.
    /// </summary>
    Task<IReadOnlyList<ResolvedTag>> ResolveOwnedByNamesAsync(
        Guid ownerId, IReadOnlyCollection<string> names, CancellationToken cancellationToken);
}

/// <summary>An owned tag resolved by name: the id to associate and the canonical stored name.</summary>
public sealed record ResolvedTag(Guid Id, string Name);
