namespace Filer.Modules.Auth.Features.Login;

/// <summary>
/// Response DTO carrying the access token. Refresh-token rotation is a planned
/// follow-up (05-security.md); the walking skeleton issues the access token only.
/// </summary>
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string TokenType = "Bearer");
