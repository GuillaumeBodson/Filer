using System.Diagnostics;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// Claims and dispatches a single job — the shared core between the polling
/// <see cref="AnalysisJobWorker"/> and the RabbitMQ consumer (ADR-008): both
/// paths run the exact same claim (single-claim stays database-enforced) and the
/// same outcome bookkeeping, so a broker-dispatched job gets the identical
/// retry/backoff/cancellation semantics and the identical linked span (ADR-013).
/// Each call runs in its own DI scope so the scoped DbContext-backed store never
/// outlives one claim.
/// </summary>
public sealed class AnalysisJobProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobsOptions> options,
    BackgroundJobsMetrics metrics,
    ILogger<AnalysisJobProcessor> logger)
{
    /// <summary>
    /// Caps the backoff exponent so an unusually high configured attempt limit can
    /// never overflow the delay arithmetic (2^10 ≈ 8.5 h on the 30 s default).
    /// </summary>
    private const int MaxBackoffExponent = 10;

    /// <summary>
    /// Claims and dispatches a single job. Returns false when no job was due.
    /// Internal so the orchestration is unit-testable against fake seams
    /// (12-testing-strategy.md).
    /// </summary>
    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();

        // Resolved before the claim: the claim itself stamps AnalysisJob.Provider
        // with the provider that will run the attempt, in the same atomic write
        // that flips the row to Running (02-data-model.md).
        var handler = scope.ServiceProvider.GetRequiredService<IAnalysisJobHandler>();

        ClaimedAnalysisJob? job = await store.ClaimNextAsync(handler.ProviderName, cancellationToken);
        if (job is null)
        {
            return false;
        }

        // Resume the correlation persisted at enqueue (ADR-013): the processing
        // span is a new root *linked* to the originating request trace — a link,
        // not a parent, because that request completed long before this attempt
        // runs. A missing or malformed context degrades to an unlinked span.
        ActivityLink[] links =
            ActivityContext.TryParse(job.CorrelationContext, null, isRemote: true, out ActivityContext enqueueContext)
                ? [new ActivityLink(enqueueContext)]
                : [];
        using Activity? activity = BackgroundJobsDiagnostics.ActivitySource.StartActivity(
            "analysisjob.process", ActivityKind.Consumer, parentContext: default, tags: null, links);
        activity?.SetTag("filer.job.id", job.JobId);
        activity?.SetTag("filer.document.id", job.DocumentId);

        // Correlate everything the job does with its id and document (04-non-functional.md).
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["JobId"] = job.JobId,
            ["DocumentId"] = job.DocumentId,
        });

        logger.JobClaimed(job.AttemptCount);

        long startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            Result<string?> result = await handler.HandleAsync(job, cancellationToken);
            TimeSpan duration = Stopwatch.GetElapsedTime(startTimestamp);

            if (result.IsSuccess)
            {
                await store.MarkSucceededAsync(job.JobId, result.Value, cancellationToken);
                metrics.JobSucceeded(duration);
                logger.JobSucceeded(duration.TotalMilliseconds);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            else if (string.Equals(result.Error!.Code, BackgroundJobsErrorCodes.DocumentGone, StringComparison.Ordinal))
            {
                // The document was deleted: cancellation, not failure (06, Job Lifecycle).
                await store.MarkCancelledAsync(job.JobId, cancellationToken);
                metrics.JobCancelled(duration);
                logger.JobCancelledDocumentGone();
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                logger.JobAttemptFailed(result.Error.Code, job.AttemptCount);
                await RecordFailureAsync(
                    store, job, $"{result.Error.Code}: {result.Error.Message}", duration, cancellationToken);
                // A failed attempt is a visibly red span (#159): code only, never content.
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown mid-flight: hand the job back so no work is lost.
            // The bookkeeping write must not itself be cancelled.
            await store.ReleaseAsync(job.JobId, CancellationToken.None);
            logger.JobReleasedOnShutdown();
            throw;
        }
        catch (Exception ex)
        {
            // Handler bug or infrastructure throw (provider timeout, storage I/O):
            // retryable like a failure Result — the next attempt may succeed (06).
            TimeSpan duration = Stopwatch.GetElapsedTime(startTimestamp);
            logger.HandlerThrew(ex);
            await RecordFailureAsync(store, job, ex.Message, duration, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
        }

        return true;
    }

    /// <summary>Requeues with backoff while attempts remain; otherwise terminal Failed (06).</summary>
    private async Task RecordFailureAsync(
        IAnalysisJobStore store,
        ClaimedAnalysisJob job,
        string error,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (job.AttemptCount < options.Value.MaxAttempts)
        {
            TimeSpan delay = BackoffDelay(job.AttemptCount);
            await store.ScheduleRetryAsync(job.JobId, error, delay, cancellationToken);
            metrics.JobRetried(duration);
            logger.RetryScheduled(job.AttemptCount, options.Value.MaxAttempts, delay);
        }
        else
        {
            await store.MarkFailedAsync(job.JobId, error, cancellationToken);
            metrics.JobFailedTerminally(duration);
            logger.JobFailedTerminally(job.AttemptCount);
        }
    }

    /// <summary>Exponential backoff: base * 2^(attempt-1), exponent capped against overflow.</summary>
    internal TimeSpan BackoffDelay(int attemptCount)
    {
        int exponent = Math.Min(Math.Max(attemptCount - 1, 0), MaxBackoffExponent);
        return options.Value.RetryBaseDelay * Math.Pow(2, exponent);
    }
}

/// <summary>
/// Log messages for <see cref="AnalysisJobProcessor"/>, co-located per the house
/// convention (13-code-quality-and-design.md). Job/document ids flow in through
/// the logging scope opened per claim; content never appears in logs.
/// </summary>
internal static partial class AnalysisJobProcessorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Claimed analysis job (attempt {AttemptCount}).")]
    public static partial void JobClaimed(this ILogger logger, int attemptCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job succeeded in {DurationMs:F0} ms.")]
    public static partial void JobSucceeded(this ILogger logger, double durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analysis job attempt {AttemptCount} failed: {ErrorCode}.")]
    public static partial void JobAttemptFailed(this ILogger logger, string errorCode, int attemptCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Analysis job retry scheduled (attempt {AttemptCount} of {MaxAttempts} failed; next try in {Delay}).")]
    public static partial void RetryScheduled(this ILogger logger, int attemptCount, int maxAttempts, TimeSpan delay);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Analysis job failed terminally after {AttemptCount} attempt(s); attempt limit exhausted.")]
    public static partial void JobFailedTerminally(this ILogger logger, int attemptCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Analysis job cancelled: its document no longer exists.")]
    public static partial void JobCancelledDocumentGone(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job released back to the queue during shutdown.")]
    public static partial void JobReleasedOnShutdown(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis job handler threw; treating the attempt as failed.")]
    public static partial void HandlerThrew(this ILogger logger, Exception exception);
}
