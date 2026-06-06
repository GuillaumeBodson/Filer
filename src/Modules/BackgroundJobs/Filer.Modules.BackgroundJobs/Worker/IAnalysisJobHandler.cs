using Filer.SharedKernel.Results;

namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// What the worker dispatches a claimed job to. This issue ships the queue and the
/// claim/dispatch loop; the real AI processing (provider call, retry/backoff,
/// idempotent result writing — 06-ai-analysis-pipeline.md) plugs in here in the
/// M5 "AI analysis pipeline" milestone.
/// </summary>
public interface IAnalysisJobHandler
{
    Task<Result> HandleAsync(ClaimedAnalysisJob job, CancellationToken cancellationToken);
}
