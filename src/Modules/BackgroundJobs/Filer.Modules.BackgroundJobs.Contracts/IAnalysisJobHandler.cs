using Filer.SharedKernel.Results;

namespace Filer.Modules.BackgroundJobs.Contracts;

/// <summary>
/// What the worker dispatches a claimed job to — the seam where the AI Analysis
/// module plugs real processing into the BackgroundJobs pipeline
/// (06-ai-analysis-pipeline.md). Lives in Contracts so the providing module
/// registers its implementation without referencing the worker's internals
/// (10-solution-structure.md); the BackgroundJobs module keeps a no-op fallback
/// behind <c>TryAddScoped</c> for hosts that wire no real handler.
/// </summary>
public interface IAnalysisJobHandler
{
    /// <summary>
    /// Diagnostic name of the analysis provider this handler dispatches to. The
    /// worker stamps it on the job row when a run starts (<c>AnalysisJob.Provider</c>,
    /// 02-data-model.md), so a stored result — or a failure — can always be
    /// attributed to the provider that produced it.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Processes one claimed job and reports the outcome the worker records:
    /// <list type="bullet">
    /// <item><description><b>Success</b> — the value is the serialized analysis
    /// result the worker writes to <c>AnalysisJob.Result</c> (JSONB), or null when
    /// the handler has nothing to record (the no-op fallback).</description></item>
    /// <item><description><b>Failure with
    /// <see cref="BackgroundJobsErrorCodes.DocumentGone"/></b> — the document was
    /// deleted before/while the job ran; the worker cancels the job instead of
    /// failing it (06, Job Lifecycle).</description></item>
    /// <item><description><b>Any other failure or thrown exception</b> — treated as
    /// retryable; the worker requeues with backoff up to the attempt limit, then
    /// marks the job terminally Failed (06, Reliability).</description></item>
    /// </list>
    /// Implementations must honour <paramref name="cancellationToken"/> mid-flight
    /// (13-code-quality-and-design.md).
    /// </summary>
    Task<Result<string?>> HandleAsync(ClaimedAnalysisJob job, CancellationToken cancellationToken);
}
