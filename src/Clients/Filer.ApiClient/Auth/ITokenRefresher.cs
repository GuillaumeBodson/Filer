namespace Filer.ApiClient.Auth;

/// <summary>
/// Exchanges the stored refresh token for a fresh <see cref="TokenPair"/> via
/// <c>/auth/refresh</c> and persists the rotated pair (05-security.md). Used by
/// <see cref="BearerTokenHandler"/> on a 401; isolated behind an interface so the
/// handler can be tested without HTTP.
/// </summary>
public interface ITokenRefresher
{
    /// <summary>
    /// Attempts a single refresh. Returns <c>true</c> and stores the rotated pair on
    /// success; <c>false</c> when there is no refresh token or the server rejects it.
    /// </summary>
    Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default);
}
