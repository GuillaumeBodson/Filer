namespace Filer.Modules.Tags.Contracts;

/// <summary>
/// Cross-module ownership check: whether the caller owns every tag in a set. The
/// Tags module implements it over its own schema; other modules consume it
/// through this contract only (10-solution-structure.md, ADR-004, ADR-009).
/// Owner-scoped by construction so cross-owner and missing tags are
/// indistinguishable — callers map <c>false</c> to the uniform 404
/// (05-security.md). Mirrors <c>IFolderOwnershipChecker</c> in the Folders module.
/// </summary>
public interface ITagOwnershipChecker
{
    /// <summary>
    /// True only if EVERY id in <paramref name="tagIds"/> is a tag owned by
    /// <paramref name="ownerId"/>. An empty set is vacuously true. A single
    /// cross-owner or missing id makes the whole call <c>false</c>, so the caller
    /// cannot tell which id failed (05-security.md).
    /// </summary>
    Task<bool> OwnsAllTagsAsync(
        Guid ownerId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);
}
