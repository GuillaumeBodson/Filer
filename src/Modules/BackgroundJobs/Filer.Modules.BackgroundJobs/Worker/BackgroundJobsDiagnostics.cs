using System.Diagnostics;

namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// The module's tracing primitives (ADR-013), mirroring
/// <see cref="BackgroundJobsMetrics.MeterName"/> for metrics: the host's
/// telemetry pipeline subscribes to <see cref="ActivitySourceName"/>; the module
/// itself emits through the BCL <see cref="System.Diagnostics.ActivitySource"/>
/// only and never references an exporter SDK (04-non-functional.md).
/// </summary>
public static class BackgroundJobsDiagnostics
{
    /// <summary>Source name a tracing exporter subscribes to.</summary>
    public const string ActivitySourceName = "Filer.BackgroundJobs";

    /// <summary>Process-wide source for the worker's job-processing spans.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
