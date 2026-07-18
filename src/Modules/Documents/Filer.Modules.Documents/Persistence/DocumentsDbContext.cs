using Filer.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

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

    /// <summary>
    /// Name of the generated full-text column on Documents (#57). Shared between
    /// the model configuration below and <see cref="EfOwnerDocumentSearch"/>,
    /// which reads the shadow property by name.
    /// </summary>
    public const string SearchVectorColumn = "SearchVector";

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Document>(document =>
        {
            document.ToTable("Documents");
            document.HasKey(d => d.Id);

            // Stored as metadata only, never used in paths (05-security.md); the
            // ceiling is shared with the upload/update validators via the entity.
            document.Property(d => d.FileName)
                .HasMaxLength(Document.MaxFileNameLength)
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

            // Full-text search vector (#57, 02-data-model.md): a stored generated
            // column over the file name and the string values of the JSONB
            // metadata, GIN-indexed. Shadow property on purpose — the vector is a
            // persistence concern; the entity never sees it. The 'simple'
            // regconfig keeps tokenization deterministic and language-neutral
            // (file names are multilingual; stemming one language would degrade
            // the others), and translate() splits '.', '_' and '-' so the parts
            // of a name like 'facture_2024.pdf' become individual lexemes instead
            // of one opaque file token. File-name matches are weighted above
            // metadata matches ('A' vs 'B') so ts_rank orders them first.
            document.Property<NpgsqlTsVector>(SearchVectorColumn)
                .HasColumnType("tsvector")
                .HasComputedColumnSql(
                    "setweight(to_tsvector('simple', translate(\"FileName\", '._-', '   ')), 'A') || " +
                    "setweight(jsonb_to_tsvector('simple', coalesce(\"Metadata\", '{}'::jsonb), '[\"string\"]'), 'B')",
                    stored: true);

            document.HasIndex(SearchVectorColumn).HasMethod("GIN");
        });

        modelBuilder.Entity<DocumentTag>(documentTag =>
        {
            documentTag.ToTable("DocumentTags");

            // Composite PK (DocumentId, TagId): exactly one row per pair, so a
            // re-add promotes the row's Source instead of inserting a duplicate
            // (02-data-model.md, ADR-009).
            documentTag.HasKey(dt => new { dt.DocumentId, dt.TagId });

            // Stored as text per 02-data-model.md; readable in SQL and stable
            // across enum reordering, exactly like Document.Status.
            documentTag.Property(dt => dt.Source)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            // FK to Document WITHIN this context: deleting a document cascades to
            // its associations. TagId stays a plain column — the Tag lives in
            // another module's schema, so no cross-schema FK or navigation (ADR-009).
            documentTag.HasOne<Document>()
                .WithMany()
                .HasForeignKey(dt => dt.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // The by-tag lookups (a tag's documents, and the tag-delete cascade
            // from #48) scan on TagId, which the composite PK does not front.
            documentTag.HasIndex(dt => dt.TagId);
        });
    }
}
