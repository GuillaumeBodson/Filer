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
}
