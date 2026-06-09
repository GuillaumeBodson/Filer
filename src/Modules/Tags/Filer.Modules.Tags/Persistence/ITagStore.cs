using Filer.Modules.Tags.Domain;

namespace Filer.Modules.Tags.Persistence;

/// <summary>
/// Persistence seam for the Tags slices, mirroring <c>IFolderStore</c> (Folders):
/// feature services stay unit-testable without a database, while the EF
/// implementation is exercised against real Postgres in Filer.IntegrationTests
/// (12-testing-strategy.md — no EF in-memory, don't mock what you own).
/// </summary>
public interface ITagStore
{
    /// <summary>
    /// Whether the caller already has a tag named <paramref name="name"/> — the
    /// per-owner uniqueness pre-check behind the 409 (02-data-model.md).
    /// </summary>
    Task<bool> NameExistsAsync(Guid ownerId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// How many of the given ids are tags owned by the caller — the
    /// every-id-owned check behind <c>ITagOwnershipChecker</c> (#49). Owner-scoped
    /// by construction, like every read on this seam, so cross-owner and missing
    /// ids are counted identically as absent (05-security.md). The caller compares
    /// the count to the distinct id count to decide ownership.
    /// </summary>
    Task<int> CountOwnedAsync(
        Guid ownerId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);

    Task AddAsync(Tag tag, CancellationToken cancellationToken);

    /// <summary>
    /// The caller's tag with the given id, or null. Owner-scoped by construction
    /// so cross-owner and missing are indistinguishable — the uniform-404 rule's
    /// single chokepoint (05-security.md). Tags are hard-deleted (#48), so unlike
    /// <c>IFolderStore.FindActiveByIdAsync</c> there is no soft-delete state to
    /// exclude.
    /// </summary>
    Task<Tag?> FindByIdAsync(Guid ownerId, Guid tagId, CancellationToken cancellationToken);

    Task UpdateAsync(Tag tag, CancellationToken cancellationToken);

    /// <summary>
    /// Every tag the caller owns, ordered by name then id so the listing is
    /// deterministic (03-api-specification.md). Owner-scoped by construction —
    /// the store cannot be queried without the caller's id (05-security.md).
    /// Tags are hard-deleted (#48), so there is no soft-delete state to exclude,
    /// unlike <c>IFolderStore.ListActiveAsync</c>.
    /// </summary>
    Task<IReadOnlyList<Tag>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
}
