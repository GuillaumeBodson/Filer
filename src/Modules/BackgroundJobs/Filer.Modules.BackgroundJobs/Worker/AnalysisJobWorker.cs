using Filer.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// Hosted-service polling loop over the DB-backed queue (06-ai-analysis-pipeline.md,
/// Worker &amp; Queue): claim the oldest due job via <see cref="AnalysisJobProcessor"/>,
/// repeat; sleep when the queue is empty. Under Db dispatch this is the primary
/// consumer (interval <see cref="BackgroundJobsOptions.PollInterval"/>); under
/// RabbitMQ dispatch (ADR-008) it degrades to the sweeper fallback (interval
/// <see cref="BackgroundJobsOptions.SweepInterval"/>) that recovers jobs whose
/// wake-up message was lost — a broker outage degrades to today's behaviour.
/// </summary>
public sealed class AnalysisJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobsOptions> options,
    IClock clock,
    BackgroundJobsMetrics metrics,
    AnalysisJobProcessor processor,
    ILogger<AnalysisJobWorker> logger) : BackgroundService
{
    /// <summary>Next time the queue depth is sampled; MinValue = sample on first iteration.</summary>
    private DateTimeOffset _nextQueueDepthReportAt = DateTimeOffset.MinValue;

    /// <summary>The idle sleep: poll cadence under Db dispatch, sweep cadence under RabbitMq (ADR-008).</summary>
    private TimeSpan IdleInterval =>
        options.Value.Queue == QueueDispatch.RabbitMq ? options.Value.SweepInterval : options.Value.PollInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.WorkerDisabled();
            return;
        }

        logger.WorkerStarted(IdleInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed;
            try
            {
                await ReportQueueDepthIfDueAsync(stoppingToken);
                processed = await processor.ProcessNextAsync(stoppingToken);
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
                    await Task.Delay(IdleInterval, stoppingToken);
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
    /// Samples and reports the queue depth at most once per
    /// <see cref="BackgroundJobsOptions.QueueDepthReportInterval"/>. Internal for
    /// deterministic unit testing with a fixed clock (12-testing-strategy.md).
    /// </summary>
    internal async Task ReportQueueDepthIfDueAsync(CancellationToken cancellationToken)
    {
        if (clock.UtcNow < _nextQueueDepthReportAt)
        {
            return;
        }

        _nextQueueDepthReportAt = clock.UtcNow + options.Value.QueueDepthReportInterval;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();

        int depth = await store.CountQueuedAsync(cancellationToken);
        metrics.RecordQueueDepth(depth);
        logger.QueueDepthSampled(depth);
    }
}

/// <summary>
/// Log messages for <see cref="AnalysisJobWorker"/>, co-located per the house
/// convention (13-code-quality-and-design.md).
/// </summary>
internal static partial class AnalysisJobWorkerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job worker is disabled by configuration.")]
    public static partial void WorkerDisabled(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job worker started (idle interval {IdleInterval}).")]
    public static partial void WorkerStarted(this ILogger logger, TimeSpan idleInterval);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job worker stopped.")]
    public static partial void WorkerStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis job worker iteration failed; continuing to poll.")]
    public static partial void WorkerIterationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis job queue depth: {QueueDepth}.")]
    public static partial void QueueDepthSampled(this ILogger logger, int queueDepth);
}
