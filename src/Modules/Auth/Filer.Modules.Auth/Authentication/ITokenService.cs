using Filer.Modules.Auth.Domain;

namespace Filer.Modules.Auth.Authentication;

/// <summary>
/// Issues access tokens for authenticated users. Kept behind an interface so the
/// token-issuing mechanism can change (e.g. future OIDC) without touching feature
/// logic (05-security.md).
/// </summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(ApplicationUser user);
}

/// <summary>A signed access token and the instant it expires (UTC).</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
