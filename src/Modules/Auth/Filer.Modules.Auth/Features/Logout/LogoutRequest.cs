namespace Filer.Modules.Auth.Features.Logout;

/// <summary>
/// Request DTO to revoke a refresh token at logout (03-api-specification.md).
/// The caller presents the refresh token it wants to retire; the access token
/// (required on the request) identifies who is asking.
/// </summary>
public sealed record LogoutRequest(string RefreshToken);
