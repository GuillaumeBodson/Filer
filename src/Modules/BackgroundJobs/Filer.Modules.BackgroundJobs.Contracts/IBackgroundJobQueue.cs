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
    /// Cancels the still-queued analysis jobs of a document (e.g. the document was
    /// deleted before processing — 06-ai-analysis-pipeline.md). Jobs already running
    /// are not interrupted here; mid-flight cancellation belongs to the worker's
    /// lifecycle handling. Returns the number of jobs cancelled; cancelling a
    /// document with no queued jobs is a success with count zero.
    /// </summary>
    Task<Result<int>> CancelQueuedForDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}
