using Filer.Modules.Folders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Folders.Persistence;

/// <summary>
/// The Folders module owns its tables in a dedicated <c>folders</c> Postgres
/// schema — one DbContext per module (10-solution-structure.md). Migrations live
/// alongside this context under Persistence/Migrations.
/// </summary>
public sealed class FoldersDbContext(DbContextOptions<FoldersDbContext> options)
    : DbContext(options)
{
    public const string Schema = "folders";

    public DbSet<Folder> Folders => Set<Folder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Folder>(folder =>
        {
            folder.ToTable("Folders");
            folder.HasKey(f => f.Id);

            // The ceiling is shared with the create validator via the entity.
            folder.Property(f => f.Name)
                .HasMaxLength(Folder.MaxNameLength)
                .IsRequired();

            // Self-reference; restrict instead of cascade — deletion is soft and
            // its non-empty semantics are owned by the delete slice (ADR-007).
            folder.HasOne<Folder>()
                .WithMany()
                .HasForeignKey(f => f.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Listing scans are owner-scoped (03-api-specification.md).
            folder.HasIndex(f => f.OwnerId);

            // Unique (OwnerId, ParentId, Name) per 02-data-model.md, as the
            // race-condition backstop behind the slice's pre-check (the slice's
            // 409 is the business path; this index keeps integrity under
            // concurrency). Two refinements over the doc's shorthand: NULLS NOT
            // DISTINCT so top-level folders (ParentId NULL) also collide, and a
            // partial filter so soft-deleted folders free their name for reuse —
            // same stance as the Documents content-hash index.
            folder.HasIndex(f => new { f.OwnerId, f.ParentId, f.Name })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasFilter("\"DeletedAt\" IS NULL");
        });
    }
}
