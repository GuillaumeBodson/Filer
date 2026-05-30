namespace Filer.Modules.Auth.Contracts;

/// <summary>
/// Public projection of an authenticated user, exposed to other modules and to
/// API clients. Entities are never exposed directly (03-api-specification.md).
/// </summary>
public sealed record UserProfile(Guid Id, string Email);
