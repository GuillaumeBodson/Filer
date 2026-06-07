using Filer.Modules.Tags.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Tags.Persistence;

/// <summary>
/// The Tags module owns its tables in a dedicated <c>tags</c> Postgres schema —
/// one DbContext per module (10-solution-structure.md). Migrations live alongside
/// this context under Persistence/Migrations.
/// </summary>
public sealed class TagsDbContext(DbContextOptions<TagsDbContext> options)
    : DbContext(options)
{
    public const string Schema = "tags";

    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Tag>(tag =>
        {
            tag.ToTable("Tags");
            tag.HasKey(t => t.Id);

            // The ceiling is shared with the create validator via the entity.
            tag.Property(t => t.Name)
                .HasMaxLength(Tag.MaxNameLength)
                .IsRequired();

            // Listing scans are owner-scoped (03-api-specification.md).
            tag.HasIndex(t => t.OwnerId);

            // Unique (OwnerId, Name) per 02-data-model.md, as the race-condition
            // backstop behind the slice's pre-check (the slice's 409 is the
            // business path; this index keeps integrity under concurrency). No
            // partial filter, unlike Folders: tag deletion is hard (#48), so
            // there are no soft-deleted rows to exempt.
            tag.HasIndex(t => new { t.OwnerId, t.Name })
                .IsUnique();
        });
    }
}
