using Filer.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// Hosted-service worker consuming the DB-backed queue (06-ai-analysis-pipeline.md,
/// Worker &amp; Queue): claim the oldest queued job, dispatch it to the handler,
/// record the outcome, repeat; sleep <see cref="BackgroundJobsOptions.PollInterval"/>
/// when the queue is empty. Each iteration runs in its own DI scope so the scoped
/// DbContext-backed store never outlives one claim.
/// </summary>
public sealed class AnalysisJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobsOptions> options,
    ILogger<AnalysisJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.WorkerDisabled();
            return;
        }

        logger.WorkerStarted(options.Value.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed;
            try
            {
                processed = await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Infrastructure failure (e.g. database unreachable). Log and keep
                // polling: the worker must outlive transient outages.
                logger.WorkerIterationFailed(ex);
                processed = false;
            }

            if (!processed)
            {
                try
                {
                    await Task.Delay(options.Value.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        logger.WorkerStopped();
    }

    /// <summary>
    /// Claims and dispatches a single job. Returns false when the queue was empty.
    /// Internal so the orchestration is unit-testable against fake seams
    /// (12-testing-strategy.md).
    /// </summary>
    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();

        ClaimedAnalysisJob? job = await store.ClaimNextAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        // Correlate everything the job does with its id and document (04-non-functional.md).
        using IDisposable? logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["JobId"] = job.JobId,
            ["DocumentId"] = job.DocumentId,
        });

        logger.JobClaimed(job.AttemptCount);

        var handler = scope.ServiceProvider.GetRequiredService<IAnalysisJobHandler>();
        try
        {
            Result result = await handler.HandleAsync(job, cancellationToken);

            if (result.IsSuccess)
            {
                await store.MarkSucceededAsync(job.JobId, cancellationToken);
                logger.JobSucceeded();
            }
            else
            {
                await store.MarkFailedAsync(job.JobId, $"{result.Error!.Code}: {result.Error.Message}", cancellationToken);
                logger.JobFailed(result.Error.Code);
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
            // Handler bug or non-Result failure: record it; the job is terminal for
            // now — retry/backoff arrives with the M5 lifecycle work (06).
            await store.MarkFailedAsync(job.JobId, ex.Message, cancellationToken);
            logger.HandlerThrew(ex);
        }

        return true;
    }
}

/// <summary>
/// Log messages for <see cref="AnalysisJobWorker"/>, co-located per the house
/// convention (13-code-quality-and-design.md). Job/document ids flow in through
/// the logging scope opened per claim; content never appears in logs.
/// </summary>
internal static partial class AnalysisJobWorkerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job worker is disabled by configuration.")]
    public static partial void WorkerDisabled(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job worker started (poll interval {PollInterval}).")]
    public static partial void WorkerStarted(this ILogger logger, TimeSpan pollInterval);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job worker stopped.")]
    public static partial void WorkerStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis job worker iteration failed; continuing to poll.")]
    public static partial void WorkerIterationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Claimed analysis job (attempt {AttemptCount}).")]
    public static partial void JobClaimed(this ILogger logger, int attemptCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job succeeded.")]
    public static partial void JobSucceeded(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analysis job failed: {ErrorCode}.")]
    public static partial void JobFailed(this ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job released back to the queue during shutdown.")]
    public static partial void JobReleasedOnShutdown(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis job handler threw; job marked failed.")]
    public static partial void HandlerThrew(this ILogger logger, Exception exception);
}
