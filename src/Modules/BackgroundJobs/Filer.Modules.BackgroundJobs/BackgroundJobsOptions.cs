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
}
