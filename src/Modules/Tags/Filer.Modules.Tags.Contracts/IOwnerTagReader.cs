namespace Filer.Modules.Tags.Contracts;

/// <summary>
/// Cross-module read: an owner's tag names as suggestion context for AI analysis
/// (06-ai-analysis-pipeline.md — providers match or extend the user's existing
/// tags). The Tags module implements it over its own schema; other modules consume
/// it through this contract only (10-solution-structure.md, ADR-004). Owner-scoped
/// by construction, like <c>ITagOwnershipChecker</c> (05-security.md).
/// </summary>
public interface IOwnerTagReader
{
    /// <summary>
    /// Every tag name the owner has, ordered by name so the context handed to a
    /// provider is deterministic (12-testing-strategy.md).
    /// </summary>
    Task<IReadOnlyList<string>> ListNamesAsync(Guid ownerId, CancellationToken cancellationToken);
}
