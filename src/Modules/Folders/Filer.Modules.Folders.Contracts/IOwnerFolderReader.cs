namespace Filer.Modules.Folders.Contracts;

/// <summary>
/// Cross-module read: an owner's active folders as suggestion context for AI
/// analysis (06-ai-analysis-pipeline.md — providers prefer the user's own
/// organisation over inventing names). The Folders module implements it over its
/// own schema; other modules consume it through this contract only
/// (10-solution-structure.md, ADR-004). Owner-scoped by construction, like
/// <c>IFolderOwnershipChecker</c> (05-security.md).
/// </summary>
public interface IOwnerFolderReader
{
    /// <summary>
    /// Every non-deleted folder the owner has, as (Id, Name), ordered by name then
    /// id so the context handed to a provider is deterministic
    /// (12-testing-strategy.md).
    /// </summary>
    Task<IReadOnlyList<OwnerFolder>> ListActiveAsync(Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>The minimal folder slice exposed across the module boundary.</summary>
public sealed record OwnerFolder(Guid Id, string Name);
