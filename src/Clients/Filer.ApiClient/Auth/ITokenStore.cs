namespace Filer.ApiClient.Auth;

/// <summary>
/// Client-side persistence for the issued <see cref="TokenPair"/>. The concrete store
/// is host-specific (browser localStorage in Filer.Web; secure native storage in the
/// future MAUI shell, RM-02), so this abstraction lives in the platform-neutral client.
/// Implementations must never log token values (05-security.md).
/// </summary>
public interface ITokenStore
{
    /// <summary>Raised after the stored tokens change (saved or cleared) so auth state can refresh.</summary>
    event EventHandler? Changed;

    /// <summary>Returns the stored tokens, or <c>null</c> when signed out.</summary>
    Task<TokenPair?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the tokens and raises <see cref="Changed"/>.</summary>
    Task SaveAsync(TokenPair tokens, CancellationToken cancellationToken = default);

    /// <summary>Removes the stored tokens and raises <see cref="Changed"/>.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
