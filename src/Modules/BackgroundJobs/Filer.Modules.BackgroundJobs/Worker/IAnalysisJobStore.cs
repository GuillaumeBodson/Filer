using Filer.Modules.BackgroundJobs.Contracts;

namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// The worker's seam onto the durable queue (12-testing-strategy.md: mock at the
/// designed seam). Claiming must be safe under concurrency — no two workers may
/// obtain the same job (06-ai-analysis-pipeline.md, Worker &amp; Queue). Every
/// completion write is guarded on the job still being Running, so a concurrent
/// cancellation (document deleted) is final and never resurrected.
/// </summary>
public interface IAnalysisJobStore
{
    /// <summary>
    /// Atomically claims the oldest due queued job: flips it to Running, stamps
    /// StartedAt, increments AttemptCount and clears NextAttemptAt. Jobs whose
    /// NextAttemptAt lies in the future (retry backoff) are skipped. Returns null
    /// when no job is due.
    /// </summary>
    Task<ClaimedAnalysisJob?> ClaimNextAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marks a claimed job Succeeded, writes the serialized analysis result to
    /// <c>AnalysisJob.Result</c> (JSONB; null when there is nothing to record) and
    /// stamps CompletedAt. A re-run overwrites the single result row, which is what
    /// keeps re-processing idempotent (06-ai-analysis-pipeline.md, Reliability).
    /// </summary>
    Task MarkSucceededAsync(Guid jobId, string? result, CancellationToken cancellationToken);

    /// <summary>Marks a claimed job terminally Failed with its failure detail and stamps CompletedAt.</summary>
    Task MarkFailedAsync(Guid jobId, string error, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a claimed job Cancelled (the document was deleted before the work
    /// could complete) and stamps CompletedAt (06-ai-analysis-pipeline.md).
    /// </summary>
    Task MarkCancelledAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a claimed job to Queued for a later retry: records the failure
    /// detail and stamps NextAttemptAt = now + <paramref name="delay"/> so the
    /// claim query leaves it alone until the backoff has elapsed
    /// (06-ai-analysis-pipeline.md, Reliability — retries with backoff).
    /// </summary>
    Task ScheduleRetryAsync(Guid jobId, string error, TimeSpan delay, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a claimed job to Queued (graceful shutdown mid-flight) so the work
    /// is not lost — durability over timeliness (06-ai-analysis-pipeline.md).
    /// </summary>
    Task ReleaseAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Number of Queued jobs (due and backing off) — the queue-depth signal the
    /// worker reports periodically (04-non-functional.md, observability).
    /// </summary>
    Task<int> CountQueuedAsync(CancellationToken cancellationToken);
}
