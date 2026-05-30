using Filer.Modules.Auth.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Auth.Persistence;

/// <summary>
/// The Auth module owns its tables in a dedicated <c>auth</c> Postgres schema —
/// one DbContext per module (10-solution-structure.md). Migrations live alongside
/// this context under Persistence/Migrations.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "auth";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.Email).IsRequired();
            user.Property(u => u.CreatedAt);
            user.Property(u => u.UpdatedAt);
        });
    }
}
