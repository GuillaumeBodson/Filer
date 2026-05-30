using Microsoft.AspNetCore.Identity;

namespace Filer.Modules.Auth.Domain;

/// <summary>
/// The authentication and ownership principal, backed by ASP.NET Core Identity
/// with a <see cref="Guid"/> key (02-data-model.md). Passwords are stored only as
/// Identity-managed hashes (05-security.md).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Reserved for the multi-tenant SaaS evolution; null in V1.</summary>
    public Guid? TenantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
