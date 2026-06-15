namespace Filer.Modules.BackgroundJobs;

/// <summary>
/// Worker configuration bound from the <c>BackgroundJobs</c> section. All values
/// have safe defaults, so the section is optional.
/// </summary>
public sealed class BackgroundJobsOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>
    /// Whether the hosted worker processes jobs. Disabled in test hosts that
    /// exercise the queue and claim path deterministically (12-testing-strategy.md).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How long the worker sleeps when the queue is empty.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Total processing attempts a job gets before a failure becomes terminal
    /// (06-ai-analysis-pipeline.md, Reliability — capped retries).
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay for retry backoff. Attempt <c>n</c> failing schedules the next
    /// try after <c>RetryBaseDelay * 2^(n-1)</c> (exponential, 06).
    /// </summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the worker samples and reports the queue depth (structured log +
    /// observable gauge, 04-non-functional.md).
    /// </summary>
    public TimeSpan QueueDepthReportInterval { get; init; } = TimeSpan.FromMinutes(1);
}
