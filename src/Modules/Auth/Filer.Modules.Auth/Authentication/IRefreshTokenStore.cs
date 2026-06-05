using Filer.Modules.Auth.Domain;

namespace Filer.Modules.Auth.Authentication;

/// <summary>
/// Server-side persistence for refresh tokens, behind an interface so feature
/// services stay unit-testable without a database and the storage mechanism can
/// change without touching slice logic (05-security.md, 13-code-quality-and-design.md).
/// Staged mutations are flushed by <see cref="SaveChangesAsync"/> so a rotation —
/// consume the old token and add its successor — commits atomically.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Stages a newly issued token for insertion.</summary>
    Task AddAsync(RefreshToken token, CancellationToken ct);

    /// <summary>Finds the stored token by its hash, or null when none matches.</summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>Returns every token in a rotation family (for theft-detection revocation).</summary>
    Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(Guid familyId, CancellationToken ct);

    /// <summary>Persists all staged changes in one unit of work.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
