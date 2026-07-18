using System.Diagnostics;
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
            // Persist the caller's W3C traceparent (Activity.Id in the default W3C
            // format) so the worker can link its span back to this trace (ADR-013).
            // The hand-off is async, so context must ride the durable row.
            CorrelationContext = Activity.Current?.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.AnalysisJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.AnalysisJobEnqueued(job.Id, job.DocumentId);

        return Result.Success(job.Id);
    }

    public async Task<Result<int>> CancelForDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            return Result.Failure<int>(Error.Validation(
                "A document id is required to cancel its analysis jobs.",
                BackgroundJobsErrorCodes.DocumentIdRequired));
        }

        DateTimeOffset now = clock.UtcNow;

        // A single set-based update over Queued and Running rows (06-ai-analysis-
        // pipeline.md: deleting a document cancels its in-flight/queued jobs). Safe
        // against a concurrently finishing worker: its completion writes are guarded
        // on Status == Running, so whichever side commits first wins and the loser
        // is a no-op — a cancelled job can never be resurrected to Succeeded/Failed.
        int cancelled = await dbContext.AnalysisJobs
            .Where(j => j.DocumentId == documentId
                && (j.Status == AnalysisJobStatus.Queued || j.Status == AnalysisJobStatus.Running))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, AnalysisJobStatus.Cancelled)
                    .SetProperty(j => j.CompletedAt, now)
                    .SetProperty(j => j.UpdatedAt, now),
                cancellationToken);

        if (cancelled > 0)
        {
            logger.JobsCancelled(cancelled, documentId);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelled {Count} analysis job(s) for document {DocumentId}.")]
    public static partial void JobsCancelled(this ILogger logger, int count, Guid documentId);
}
