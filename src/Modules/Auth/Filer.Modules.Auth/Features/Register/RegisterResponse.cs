namespace Filer.Modules.Auth.Features.Register;

/// <summary>Response DTO returned on successful registration.</summary>
public sealed record RegisterResponse(Guid Id, string Email);
