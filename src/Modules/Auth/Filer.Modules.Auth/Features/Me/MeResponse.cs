namespace Filer.Modules.Auth.Features.Me;

/// <summary>Current user's profile (03-api-specification.md: GET /auth/me).</summary>
public sealed record MeResponse(Guid Id, string Email);
