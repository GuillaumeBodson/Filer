namespace Filer.Modules.Auth.Features.Register;

/// <summary>Request DTO for creating an account (03-api-specification.md).</summary>
public sealed record RegisterRequest(string Email, string Password);
