namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// The worker's seam onto the durable queue (12-testing-strategy.md: mock at the
/// designed seam). Claiming must be safe under concurrency — no two workers may
/// obtain the same job (06-ai-analysis-pipeline.md, Worker &amp; Queue).
/// </summary>
public interface IAnalysisJobStore
{
    /// <summary>
    /// Atomically claims the oldest queued job: flips it to Running, stamps
    /// StartedAt and increments AttemptCount. Returns null when the queue is empty.
    /// </summary>
    Task<ClaimedAnalysisJob?> ClaimNextAsync(CancellationToken cancellationToken);

    /// <summary>Marks a claimed job Succeeded and stamps CompletedAt.</summary>
    Task MarkSucceededAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Marks a claimed job Failed with its failure detail and stamps CompletedAt.</summary>
    Task MarkFailedAsync(Guid jobId, string error, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a claimed job to Queued (graceful shutdown mid-flight) so the work
    /// is not lost — durability over timeliness (06-ai-analysis-pipeline.md).
    /// </summary>
    Task ReleaseAsync(Guid jobId, CancellationToken cancellationToken);
}

/// <summary>The slice of a claimed job the handler needs; the entity stays module-internal.</summary>
public sealed record ClaimedAnalysisJob(Guid JobId, Guid DocumentId, int AttemptCount);
