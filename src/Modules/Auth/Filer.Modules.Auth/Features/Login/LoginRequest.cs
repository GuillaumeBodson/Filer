namespace Filer.Modules.Auth.Features.Login;

/// <summary>Request DTO to obtain an access token (03-api-specification.md).</summary>
public sealed record LoginRequest(string Email, string Password);
