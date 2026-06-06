using Filer.Modules.BackgroundJobs.Domain;
using Filer.Modules.BackgroundJobs.Worker;
using Filer.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.BackgroundJobs.Persistence;

/// <summary>
/// Poll/claim with row locking over the AnalysisJob table (06-ai-analysis-pipeline.md,
/// Worker &amp; Queue). The claim is a single atomic
/// <c>UPDATE … WHERE Id = (SELECT … FOR UPDATE SKIP LOCKED) RETURNING *</c>: a row
/// is either claimed exactly once or skipped, so two workers can never run the same
/// job, and an uncommitted claim simply stays Queued — a crash loses no work.
/// </summary>
public sealed class EfAnalysisJobStore(JobsDbContext dbContext, IClock clock) : IAnalysisJobStore
{
    // Status values as persisted (enum-to-string conversion in JobsDbContext).
    private static readonly string Queued = nameof(AnalysisJobStatus.Queued);
    private static readonly string Running = nameof(AnalysisJobStatus.Running);

    public async Task<ClaimedAnalysisJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        // Raw SQL because the locking claim cannot be expressed in LINQ. The
        // interpolated values become parameters (FromSql), never string concat.
        List<AnalysisJob> claimed = await dbContext.AnalysisJobs
            .FromSql($"""
                UPDATE jobs."AnalysisJobs"
                SET "Status" = {Running},
                    "StartedAt" = {now},
                    "UpdatedAt" = {now},
                    "AttemptCount" = "AttemptCount" + 1
                WHERE "Id" = (
                    SELECT "Id"
                    FROM jobs."AnalysisJobs"
                    WHERE "Status" = {Queued}
                    ORDER BY "CreatedAt"
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED)
                RETURNING *
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (claimed.Count == 0)
        {
            return null;
        }

        AnalysisJob job = claimed[0];
        return new ClaimedAnalysisJob(job.Id, job.DocumentId, job.AttemptCount);
    }

    public Task MarkSucceededAsync(Guid jobId, CancellationToken cancellationToken) =>
        CompleteAsync(jobId, AnalysisJobStatus.Succeeded, error: null, cancellationToken);

    public Task MarkFailedAsync(Guid jobId, string error, CancellationToken cancellationToken) =>
        CompleteAsync(jobId, AnalysisJobStatus.Failed, error, cancellationToken);

    public async Task ReleaseAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        await dbContext.AnalysisJobs
            .Where(j => j.Id == jobId && j.Status == AnalysisJobStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, AnalysisJobStatus.Queued)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.UpdatedAt, now),
                cancellationToken);
    }

    private async Task CompleteAsync(
        Guid jobId,
        AnalysisJobStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        await dbContext.AnalysisJobs
            .Where(j => j.Id == jobId && j.Status == AnalysisJobStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, status)
                    .SetProperty(j => j.Error, error)
                    .SetProperty(j => j.CompletedAt, now)
                    .SetProperty(j => j.UpdatedAt, now),
                cancellationToken);
    }
}
