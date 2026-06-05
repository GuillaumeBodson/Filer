namespace Filer.Modules.Auth.Features.Refresh;

/// <summary>
/// Response DTO carrying the rotated token pair. Each refresh issues a new access
/// token and a new refresh token; the presented refresh token is consumed and must
/// not be reused (05-security.md).
/// </summary>
public sealed record RefreshResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string TokenType = "Bearer");
