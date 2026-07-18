using System.Diagnostics.CodeAnalysis;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.BackgroundJobs.Messaging;

/// <summary>
/// Outbox-relay decorator over the DB queue (ADR-008): the row is inserted and
/// committed exactly as before — durability is unchanged — then a broker wake-up
/// is published. A publish failure is logged and swallowed: the job is already
/// durable, and the sweeper (the retained polling loop) recovers it, so a broker
/// outage degrades to Db-dispatch behaviour instead of failing uploads.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Implements IBackgroundJobQueue, whose name is fixed by the architecture docs (06, 10).")]
internal sealed class RabbitMqBackgroundJobQueue(
    IBackgroundJobQueue inner,
    IAnalysisJobDispatcher dispatcher,
    ILogger<RabbitMqBackgroundJobQueue> logger) : IBackgroundJobQueue
{
    public async Task<Result<Guid>> EnqueueAnalysisAsync(Guid documentId, CancellationToken cancellationToken)
    {
        Result<Guid> result = await inner.EnqueueAnalysisAsync(documentId, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        try
        {
            await dispatcher.PublishJobReadyAsync(result.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Deliberately broad: whatever the broker client throws, the accepted
            // job must not be failed retroactively — the row is committed and the
            // sweeper delivers it (ADR-008, broker-down degradation).
            logger.WakeUpPublishFailed(ex, result.Value);
        }

        return result;
    }

    // Cancellation is a row-state flip; the broker carries no cancellable state.
    public Task<Result<int>> CancelForDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        inner.CancelForDocumentAsync(documentId, cancellationToken);
}

/// <summary>
/// Log messages for <see cref="RabbitMqBackgroundJobQueue"/>, co-located per the
/// house convention (13-code-quality-and-design.md). Ids only — never content.
/// </summary>
internal static partial class RabbitMqBackgroundJobQueueLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Publishing the wake-up for analysis job {JobId} failed; the sweeper will recover it.")]
    public static partial void WakeUpPublishFailed(this ILogger logger, Exception exception, Guid jobId);
}
