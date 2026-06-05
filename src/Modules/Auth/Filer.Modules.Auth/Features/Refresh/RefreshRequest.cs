namespace Filer.Modules.Auth.Features.Refresh;

/// <summary>Request DTO to exchange a refresh token for a new token pair (03-api-specification.md).</summary>
public sealed record RefreshRequest(string RefreshToken);
