using Filer.Modules.BackgroundJobs.Contracts;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// Fallback handler: proves the claim/dispatch pipeline end to end without doing
/// analysis. The real processing lives in the AI Analysis module
/// (<c>AnalysisJobHandler</c>, 06-ai-analysis-pipeline.md), registered before this
/// module so the <c>TryAddScoped</c> fallback never wins in the production host;
/// it remains for hosts and tests that wire no AI module. Succeeds with no result
/// payload, so <c>AnalysisJob.Result</c> stays null.
/// </summary>
public sealed class NoOpAnalysisJobHandler(ILogger<NoOpAnalysisJobHandler> logger) : IAnalysisJobHandler
{
    /// <summary>Stamped on claimed rows so a no-op run is distinguishable from a real provider's.</summary>
    public string ProviderName => "None";

    public Task<Result<string?>> HandleAsync(ClaimedAnalysisJob job, CancellationToken cancellationToken)
    {
        logger.NoOpHandlerInvoked(job.JobId, job.DocumentId, job.AttemptCount);

        return Task.FromResult(Result.Success<string?>(null));
    }
}

/// <summary>
/// Log messages for <see cref="NoOpAnalysisJobHandler"/>, co-located per the house
/// convention (13-code-quality-and-design.md).
/// </summary>
internal static partial class NoOpAnalysisJobHandlerLog
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No-op analysis handler invoked for job {JobId} (document {DocumentId}, attempt {AttemptCount}); " +
                  "no AI analysis module is registered in this host.")]
    public static partial void NoOpHandlerInvoked(this ILogger logger, Guid jobId, Guid documentId, int attemptCount);
}
