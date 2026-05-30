namespace Filer.Modules.Auth.Contracts;

/// <summary>
/// Claim names written into the access token by the Auth module and read back by
/// the host when building the current-user context. Centralised here so the
/// issuer and the reader never drift apart.
/// </summary>
public static class AuthClaimTypes
{
    /// <summary>Subject — the user's id (<c>sub</c>).</summary>
    public const string Subject = "sub";

    /// <summary>The user's email.</summary>
    public const string Email = "email";

    /// <summary>Reserved for the multi-tenant SaaS evolution (02-data-model.md).</summary>
    public const string TenantId = "tenant_id";
}
