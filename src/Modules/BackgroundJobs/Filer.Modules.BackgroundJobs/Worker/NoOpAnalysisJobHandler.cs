using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.BackgroundJobs.Worker;

/// <summary>
/// V1 placeholder handler: proves the claim/dispatch pipeline end to end without
/// doing analysis. Replaced by the real AI processing in the M5 milestone
/// (06-ai-analysis-pipeline.md); until then claimed jobs complete trivially.
/// </summary>
public sealed class NoOpAnalysisJobHandler(ILogger<NoOpAnalysisJobHandler> logger) : IAnalysisJobHandler
{
    public Task<Result> HandleAsync(ClaimedAnalysisJob job, CancellationToken cancellationToken)
    {
        logger.NoOpHandlerInvoked(job.JobId, job.DocumentId, job.AttemptCount);

        return Task.FromResult(Result.Success());
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
                  "real processing arrives with the AI analysis milestone.")]
    public static partial void NoOpHandlerInvoked(this ILogger logger, Guid jobId, Guid documentId, int attemptCount);
}
