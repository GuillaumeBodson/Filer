using Filer.Modules.BackgroundJobs.Contracts;
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
/// job, and an uncommitted claim simply stays Queued — a crash loses no work. Rows
/// whose <c>NextAttemptAt</c> lies in the future are retries backing off and are
/// skipped until due. Every completion write is guarded on the row still being
/// Running, so a concurrent cancellation (document deleted) is final.
/// </summary>
public sealed class EfAnalysisJobStore(JobsDbContext dbContext, IClock clock) : IAnalysisJobStore
{
    // Status values as persisted (enum-to-string conversion in JobsDbContext).
    private static readonly string Queued = nameof(AnalysisJobStatus.Queued);
    private static readonly string Running = nameof(AnalysisJobStatus.Running);

    public async Task<ClaimedAnalysisJob?> ClaimNextAsync(string providerName, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        // Raw SQL because the locking claim cannot be expressed in LINQ. The
        // interpolated values become parameters (FromSql), never string concat.
        // Provider is (re-)stamped on every claim, so after a retry the row names
        // the provider of the latest attempt (02-data-model.md).
        List<AnalysisJob> claimed = await dbContext.AnalysisJobs
            .FromSql($"""
                UPDATE jobs."AnalysisJobs"
                SET "Status" = {Running},
                    "StartedAt" = {now},
                    "UpdatedAt" = {now},
                    "Provider" = {providerName},
                    "NextAttemptAt" = NULL,
                    "AttemptCount" = "AttemptCount" + 1
                WHERE "Id" = (
                    SELECT "Id"
                    FROM jobs."AnalysisJobs"
                    WHERE "Status" = {Queued}
                      AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {now})
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

    public Task MarkSucceededAsync(Guid jobId, string? result, CancellationToken cancellationToken) =>
        CompleteAsync(jobId, AnalysisJobStatus.Succeeded, error: null, result, cancellationToken);

    public Task MarkFailedAsync(Guid jobId, string error, CancellationToken cancellationToken) =>
        CompleteAsync(jobId, AnalysisJobStatus.Failed, error, result: null, cancellationToken);

    public Task MarkCancelledAsync(Guid jobId, CancellationToken cancellationToken) =>
        CompleteAsync(jobId, AnalysisJobStatus.Cancelled, error: null, result: null, cancellationToken);

    public async Task ScheduleRetryAsync(Guid jobId, string error, TimeSpan delay, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        // Back to Queued with the failure recorded and the backoff stamped; the
        // Running guard means a concurrently cancelled job is never requeued.
        await dbContext.AnalysisJobs
            .Where(j => j.Id == jobId && j.Status == AnalysisJobStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, AnalysisJobStatus.Queued)
                    .SetProperty(j => j.Error, error)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.NextAttemptAt, now + delay)
                    .SetProperty(j => j.UpdatedAt, now),
                cancellationToken);
    }

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

    public Task<int> CountQueuedAsync(CancellationToken cancellationToken) =>
        dbContext.AnalysisJobs
            .AsNoTracking()
            .CountAsync(j => j.Status == AnalysisJobStatus.Queued, cancellationToken);

    private async Task CompleteAsync(
        Guid jobId,
        AnalysisJobStatus status,
        string? error,
        string? result,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        // Writing Result here (single JSONB overwrite per job) is what keeps a
        // re-run idempotent: there is exactly one result row to replace, never an
        // accumulating list of suggestions (06, Reliability — idempotency).
        await dbContext.AnalysisJobs
            .Where(j => j.Id == jobId && j.Status == AnalysisJobStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, status)
                    .SetProperty(j => j.Error, error)
                    .SetProperty(j => j.Result, result)
                    .SetProperty(j => j.CompletedAt, now)
                    .SetProperty(j => j.UpdatedAt, now),
                cancellationToken);
    }
}
