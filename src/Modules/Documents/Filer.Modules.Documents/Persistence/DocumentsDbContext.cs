using Filer.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Documents.Persistence;

/// <summary>
/// The Documents module owns its tables in a dedicated <c>documents</c> Postgres
/// schema — one DbContext per module (10-solution-structure.md). Migrations live
/// alongside this context under Persistence/Migrations.
/// </summary>
public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options)
    : DbContext(options)
{
    public const string Schema = "documents";

    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Document>(document =>
        {
            document.ToTable("Documents");
            document.HasKey(d => d.Id);

            // Stored as metadata only, never used in paths (05-security.md); bound
            // to the same ceiling the upload validator enforces.
            document.Property(d => d.FileName)
                .HasMaxLength(255)
                .IsRequired();

            document.Property(d => d.ContentType)
                .HasMaxLength(255)
                .IsRequired();

            // Opaque provider key: 64 hex chars today (Storage module), with slack
            // for future providers (07-storage-and-deployment.md).
            document.Property(d => d.StorageKey)
                .HasMaxLength(128)
                .IsRequired();

            // SHA-256 as lowercase hex is exactly 64 chars (02-data-model.md).
            document.Property(d => d.ContentHash)
                .HasMaxLength(64)
                .IsRequired();

            // Stored as text per 02-data-model.md; readable in SQL and stable
            // across enum reordering.
            document.Property(d => d.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            // Flexible extra attributes land as JSONB (02-data-model.md).
            document.Property(d => d.Metadata).HasColumnType("jsonb");

            // Listing scans are owner-scoped (03-api-specification.md).
            document.HasIndex(d => d.OwnerId);
            document.HasIndex(d => new { d.OwnerId, d.FolderId });

            // Duplicate lookup considers only live rows, hence the partial index
            // (02-data-model.md). Not unique: after a 409 the client may still
            // choose to keep both copies (03-api-specification.md, upload behavior).
            document.HasIndex(d => new { d.OwnerId, d.ContentHash })
                .HasFilter("\"DeletedAt\" IS NULL");
        });
    }
}
