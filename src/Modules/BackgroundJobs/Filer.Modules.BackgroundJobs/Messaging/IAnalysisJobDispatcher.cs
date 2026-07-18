namespace Filer.Modules.BackgroundJobs.Messaging;

/// <summary>
/// Seam for publishing the broker wake-up after the outbox row commits (ADR-008).
/// The message is a bare "job {id} ready" signal — the AnalysisJobs row remains
/// the authoritative job state and trace-context carrier (ADR-013); losing a
/// message loses nothing, because the sweeper recovers the committed row.
/// </summary>
internal interface IAnalysisJobDispatcher
{
    Task PublishJobReadyAsync(Guid jobId, CancellationToken cancellationToken);
}
