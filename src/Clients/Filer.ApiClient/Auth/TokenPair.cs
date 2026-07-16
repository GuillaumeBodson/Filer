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
    DateTimeOffset? RefreshTokenExpiresAt);
