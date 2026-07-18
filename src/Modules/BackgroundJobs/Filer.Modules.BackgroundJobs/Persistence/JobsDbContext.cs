using Filer.Modules.BackgroundJobs.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.BackgroundJobs.Persistence;

/// <summary>
/// The BackgroundJobs module owns its tables in a dedicated <c>jobs</c> Postgres
/// schema — one DbContext per module (10-solution-structure.md). Migrations live
/// alongside this context under Persistence/Migrations.
/// </summary>
public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options)
    : DbContext(options)
{
    public const string Schema = "jobs";

    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AnalysisJob>(job =>
        {
            job.ToTable("AnalysisJobs");
            job.HasKey(j => j.Id);

            // Stored as text per 02-data-model.md; readable in SQL and stable
            // across enum reordering.
            job.Property(j => j.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            job.Property(j => j.Provider).HasMaxLength(128);

            // A W3C traceparent is 55 chars; the headroom leaves room for richer
            // context (tracestate/baggage) without a schema change (ADR-013).
            job.Property(j => j.CorrelationContext).HasMaxLength(512);

            // AI suggestions land as JSONB (02-data-model.md, PostgreSQL notes).
            job.Property(j => j.Result).HasColumnType("jsonb");

            // The claim query scans for the oldest queued job — index exactly that.
            job.HasIndex(j => new { j.Status, j.CreatedAt });

            // Cancellation and status lookups are by document.
            job.HasIndex(j => j.DocumentId);
        });
    }
}
