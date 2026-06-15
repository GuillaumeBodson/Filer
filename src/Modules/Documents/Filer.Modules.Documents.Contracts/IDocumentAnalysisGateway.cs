namespace Filer.Modules.Documents.Contracts;

/// <summary>
/// The cross-module seam the AI analysis worker reads and advances documents
/// through (06-ai-analysis-pipeline.md): the handler loads what it needs to build
/// an analysis request, and marks the document Ready when analysis succeeds. A
/// narrow contract owned by the module that owns the data, mirroring
/// <c>IFolderDocumentRemover</c> and <c>IDocumentTagRemover</c>
/// (10-solution-structure.md). Unlike the user-facing seams this one is not
/// owner-scoped: the background worker acts on a job's document id with no
/// caller principal (05-security.md does not apply — nothing here reaches a
/// client); the snapshot's <see cref="AnalysisDocumentSnapshot.OwnerId"/> is how
/// the worker scopes its *other* context reads (folders, tags) to the right user.
/// </summary>
public interface IDocumentAnalysisGateway
{
    /// <summary>
    /// The analysis-relevant slice of a non-deleted document, or null when the
    /// document is missing or soft-deleted — the signal that its analysis job
    /// should be cancelled, not failed (06, Job Lifecycle).
    /// </summary>
    Task<AnalysisDocumentSnapshot?> FindForAnalysisAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a non-deleted document's <c>Status</c> to <c>Ready</c> after a
    /// successful analysis (06, Job Lifecycle step 3). Idempotent: an already-Ready
    /// document stays Ready, and a document deleted in the meantime is left alone.
    /// </summary>
    Task MarkReadyAsync(Guid documentId, CancellationToken cancellationToken);
}

/// <summary>
/// What the analysis handler needs to know about one document (06): identity and
/// owner, the name/type signals for suggestions, and the opaque storage key for
/// text extraction via <c>IFileStorageProvider</c> (07-storage-and-deployment.md).
/// </summary>
public sealed record AnalysisDocumentSnapshot(
    Guid DocumentId,
    Guid OwnerId,
    string FileName,
    string ContentType,
    string StorageKey);
