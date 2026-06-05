using Filer.Modules.Auth.Domain;

namespace Filer.Modules.Auth.Authentication;

/// <summary>
/// Issues access and refresh tokens for authenticated users. Kept behind an
/// interface so the token-issuing mechanism can change (e.g. future OIDC) without
/// touching feature logic (05-security.md).
/// </summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user);

    /// <summary>
    /// Mints a fresh opaque refresh token. Returns the raw value (handed to the
    /// client exactly once) together with the SHA-256 hash to persist and the
    /// expiry instant. The raw value is never stored (05-security.md).
    /// </summary>
    RefreshTokenMaterial CreateRefreshToken();

    /// <summary>
    /// Hashes a raw refresh token presented by a client so it can be matched
    /// against the stored hash. Same algorithm as <see cref="CreateRefreshToken"/>.
    /// </summary>
    string HashRefreshToken(string rawToken);
}

/// <summary>A signed access token and the instant it expires (UTC).</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// A freshly minted refresh token: the raw value to return to the caller, the hash
/// to persist, and its expiry. <see cref="RawToken"/> never touches the database.
/// </summary>
public sealed record RefreshTokenMaterial(string RawToken, string TokenHash, DateTimeOffset ExpiresAt);
