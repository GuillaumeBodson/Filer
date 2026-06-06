using System.Diagnostics.CodeAnalysis;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Domain;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.BackgroundJobs.Queueing;

/// <summary>
/// DB-backed implementation of <see cref="IBackgroundJobQueue"/>: enqueueing is an
/// insert into the module-owned AnalysisJob table, which the worker polls/claims
/// (06-ai-analysis-pipeline.md, Worker &amp; Queue). Durability falls out of the
/// row being committed before the call returns — a crash loses no accepted work.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Implements IBackgroundJobQueue, whose name is fixed by the architecture docs (06, 10).")]
public sealed class EfBackgroundJobQueue(
    JobsDbContext dbContext,
    IClock clock,
    ILogger<EfBackgroundJobQueue> logger) : IBackgroundJobQueue
{
    public async Task<Result<Guid>> EnqueueAnalysisAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            return Result.Failure<Guid>(Error.Validation(
                "A document id is required to enqueue an analysis job.",
                BackgroundJobsErrorCodes.DocumentIdRequired));
        }

        DateTimeOffset now = clock.UtcNow;
        var job = new AnalysisJob
        {
            DocumentId = documentId,
            Status = AnalysisJobStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.AnalysisJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.AnalysisJobEnqueued(job.Id, job.DocumentId);

        return Result.Success(job.Id);
    }

    public async Task<Result<int>> CancelQueuedForDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            return Result.Failure<int>(Error.Validation(
                "A document id is required to cancel its queued jobs.",
                BackgroundJobsErrorCodes.DocumentIdRequired));
        }

        DateTimeOffset now = clock.UtcNow;

        // A single set-based update: only rows still Queued flip to Cancelled, so a
        // job a worker claimed in the meantime (Running) is untouched — mid-flight
        // cancellation is the worker's concern (06-ai-analysis-pipeline.md).
        int cancelled = await dbContext.AnalysisJobs
            .Where(j => j.DocumentId == documentId && j.Status == AnalysisJobStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, AnalysisJobStatus.Cancelled)
                    .SetProperty(j => j.CompletedAt, now)
                    .SetProperty(j => j.UpdatedAt, now),
                cancellationToken);

        if (cancelled > 0)
        {
            logger.QueuedJobsCancelled(cancelled, documentId);
        }

        return Result.Success(cancelled);
    }
}

/// <summary>
/// Log messages for <see cref="EfBackgroundJobQueue"/>, co-located per the house
/// convention (13-code-quality-and-design.md). Identify work by ids only — job
/// content never appears in logs.
/// </summary>
internal static partial class EfBackgroundJobQueueLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Enqueued analysis job {JobId} for document {DocumentId}.")]
    public static partial void AnalysisJobEnqueued(this ILogger logger, Guid jobId, Guid documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelled {Count} queued analysis job(s) for document {DocumentId}.")]
    public static partial void QueuedJobsCancelled(this ILogger logger, int count, Guid documentId);
}
