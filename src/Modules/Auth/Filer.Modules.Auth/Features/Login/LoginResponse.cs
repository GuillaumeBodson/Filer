namespace Filer.Modules.Auth.Features.Login;

/// <summary>
/// Response DTO carrying the issued tokens (03-api-specification.md). The access
/// token is the short-lived bearer for API calls; the refresh token is the
/// long-lived opaque value exchanged at <c>/auth/refresh</c> for a new pair
/// (05-security.md). The raw refresh token is returned here exactly once and is
/// never persisted in clear.
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string TokenType = "Bearer");
