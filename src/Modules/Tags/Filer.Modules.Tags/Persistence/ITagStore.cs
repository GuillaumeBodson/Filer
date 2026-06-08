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

    Task AddAsync(Tag tag, CancellationToken cancellationToken);

    /// <summary>
    /// Every tag the caller owns, ordered by name then id so the listing is
    /// deterministic (03-api-specification.md). Owner-scoped by construction —
    /// the store cannot be queried without the caller's id (05-security.md).
    /// Tags are hard-deleted (#48), so there is no soft-delete state to exclude,
    /// unlike <c>IFolderStore.ListActiveAsync</c>.
    /// </summary>
    Task<IReadOnlyList<Tag>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
}
