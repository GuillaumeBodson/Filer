namespace Filer.Modules.BackgroundJobs.Contracts;

/// <summary>
/// Read-only cross-module surface over a document's analysis jobs: the status
/// slice (<c>GET /documents/{id}/analysis</c>) and the apply slice
/// (<c>POST /documents/{id}/analysis/apply</c>) consult the LATEST job through
/// this contract only (06-ai-analysis-pipeline.md, Applying Suggestions;
/// 10-solution-structure.md). Deliberately minimal: no job listing, no error
/// text — clients never see raw provider failures (05-security.md), "failed"
/// as a status is the whole story.
/// </summary>
public interface IAnalysisJobReader
{
    /// <summary>
    /// The most recent analysis job for the document (by creation time), or null
    /// when the document was never queued. Not owner-scoped: jobs carry no owner,
    /// so callers MUST resolve the document through their own ownership check
    /// first (05-security.md) — by the time this runs the document is proven to
    /// be the caller's.
    /// </summary>
    Task<AnalysisJobSnapshot?> FindLatestForDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}

/// <summary>
/// The slice of an analysis job other modules may see (06-ai-analysis-pipeline.md).
/// Carries no <c>Error</c> on purpose: provider failure detail stays inside the
/// BackgroundJobs module and is never surfaced to clients (05-security.md).
/// </summary>
/// <param name="JobId">The job's identifier, as returned by the upload response.</param>
/// <param name="Status">Lifecycle state, re-exposed as the Contracts-level <see cref="AnalysisJobState"/>.</param>
/// <param name="Result">The provider-neutral suggestions as raw JSON, present only once succeeded.</param>
/// <param name="CompletedAt">When the job reached a terminal state, or null while pending.</param>
public sealed record AnalysisJobSnapshot(
    Guid JobId,
    AnalysisJobState Status,
    string? Result,
    DateTimeOffset? CompletedAt);

/// <summary>
/// Contracts-level mirror of the job lifecycle (06-ai-analysis-pipeline.md, Job
/// Lifecycle). A separate enum on purpose: the module's persisted Domain enum
/// must not leak across the boundary, so the implementation maps explicitly.
/// </summary>
public enum AnalysisJobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
