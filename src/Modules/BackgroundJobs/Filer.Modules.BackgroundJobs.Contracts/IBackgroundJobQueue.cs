using System.Diagnostics.CodeAnalysis;
using Filer.SharedKernel.Results;

namespace Filer.Modules.BackgroundJobs.Contracts;

/// <summary>
/// The asynchronous hand-off between modules (10-solution-structure.md): a feature
/// slice persists its own state, enqueues work here, and returns immediately —
/// analysis never runs inline with a request (06-ai-analysis-pipeline.md, 08).
/// The queue is durable: an accepted job survives a crash and is eventually
/// processed by the background worker.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The abstraction IS a queue — the name is fixed by the architecture docs " +
                    "(06-ai-analysis-pipeline.md, 10-solution-structure.md) and asserted by tests.")]
public interface IBackgroundJobQueue
{
    /// <summary>
    /// Enqueues an AI-analysis job for a document. Returns the job id the caller
    /// surfaces to clients (the upload response carries it — 03-api-specification.md).
    /// </summary>
    Task<Result<Guid>> EnqueueAnalysisAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a document's queued and running analysis jobs (deleting a document
    /// cancels its in-flight/queued work — 06-ai-analysis-pipeline.md). The flip to
    /// Cancelled is immediate and final: the worker's completion writes are guarded
    /// on a job still being Running, so a worker that finishes a just-cancelled job
    /// records nothing. The worker is not interrupted mid-flight here — honoring
    /// cancellation inside a running attempt belongs to the worker's lifecycle
    /// handling. Returns the number of jobs cancelled; cancelling a document with
    /// no active jobs is a success with count zero.
    /// </summary>
    Task<Result<int>> CancelForDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}
