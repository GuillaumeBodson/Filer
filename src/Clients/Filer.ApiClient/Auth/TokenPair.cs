namespace Filer.ApiClient.Auth;

/// <summary>
/// The token pair issued by <c>/auth/login</c> and rotated by <c>/auth/refresh</c>
/// (05-security.md): a short-lived access token sent as the bearer, and a long-lived
/// opaque refresh token exchanged for a new pair. Persisted by an <see cref="ITokenStore"/>.
/// </summary>
public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset? AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAt)
{
    /// <summary>
    /// Whether the access token is expired (or within <paramref name="skew"/> of it),
    /// so callers can refresh pre-emptively instead of waiting for a 401.
    /// </summary>
    public bool IsAccessTokenExpired(DateTimeOffset now, TimeSpan skew) =>
        AccessTokenExpiresAt is { } expiresAt && now >= expiresAt - skew;
}
