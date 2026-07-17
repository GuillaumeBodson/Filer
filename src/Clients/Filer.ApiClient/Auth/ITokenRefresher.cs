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
    /// Attempts a single refresh. Stores the rotated pair and returns
    /// <see cref="TokenRefreshResult.Refreshed"/> on success;
    /// <see cref="TokenRefreshResult.Rejected"/> when there is no refresh token or the
    /// server rejects it (401/403); <see cref="TokenRefreshResult.TransientFailure"/>
    /// when the call fails without condemning the token (5xx, malformed response) so
    /// the caller keeps the session (#167).
    /// </summary>
    Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
}
