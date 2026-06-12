using System.Diagnostics.Metrics;

namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// The module's metrics (04-non-functional.md; 06-ai-analysis-pipeline.md,
/// Reliability — observability): outcome counters, a processing-duration
/// histogram tagged by outcome, and a queue-depth gauge. The gauge is observable
/// but must never query the database from the collection callback, so the worker
/// samples the depth periodically and this type replays the last sample; until
/// the first sample the gauge reports nothing. Singleton — instruments are
/// process-wide by design.
/// </summary>
public sealed class BackgroundJobsMetrics
{
    /// <summary>Meter name a metrics exporter subscribes to.</summary>
    public const string MeterName = "Filer.BackgroundJobs";

    private const string OutcomeTagName = "filer.job.outcome";

    private readonly Counter<long> _succeeded;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _retried;
    private readonly Counter<long> _cancelled;
    private readonly Histogram<double> _duration;

    /// <summary>Last sampled queue depth; negative = not sampled yet.</summary>
    private int _queueDepth = -1;

    public BackgroundJobsMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        // The factory owns the meter's lifetime alongside the container's.
        Meter meter = meterFactory.Create(MeterName);

        _succeeded = meter.CreateCounter<long>(
            "filer.background_jobs.succeeded", unit: "{job}",
            description: "Analysis jobs that completed successfully.");
        _failed = meter.CreateCounter<long>(
            "filer.background_jobs.failed", unit: "{job}",
            description: "Analysis jobs that failed terminally (attempt limit exhausted).");
        _retried = meter.CreateCounter<long>(
            "filer.background_jobs.retried", unit: "{job}",
            description: "Analysis job attempts requeued for a backoff retry.");
        _cancelled = meter.CreateCounter<long>(
            "filer.background_jobs.cancelled", unit: "{job}",
            description: "Analysis jobs cancelled because their document was deleted.");
        _duration = meter.CreateHistogram<double>(
            "filer.background_jobs.duration", unit: "s",
            description: "Processing duration of one job attempt, tagged by outcome.");
        meter.CreateObservableGauge(
            "filer.background_jobs.queue_depth", ObserveQueueDepth, unit: "{job}",
            description: "Queued analysis jobs (due and backing off), sampled periodically by the worker.");
    }

    public void JobSucceeded(TimeSpan duration) => Record(_succeeded, "succeeded", duration);

    public void JobFailedTerminally(TimeSpan duration) => Record(_failed, "failed", duration);

    public void JobRetried(TimeSpan duration) => Record(_retried, "retried", duration);

    public void JobCancelled(TimeSpan duration) => Record(_cancelled, "cancelled", duration);

    public void RecordQueueDepth(int depth) => Volatile.Write(ref _queueDepth, depth);

    private void Record(Counter<long> outcomeCounter, string outcome, TimeSpan duration)
    {
        var outcomeTag = new KeyValuePair<string, object?>(OutcomeTagName, outcome);
        outcomeCounter.Add(1);
        _duration.Record(duration.TotalSeconds, outcomeTag);
    }

    private IEnumerable<Measurement<int>> ObserveQueueDepth()
    {
        int depth = Volatile.Read(ref _queueDepth);
        return depth < 0 ? [] : [new Measurement<int>(depth)];
    }
}
