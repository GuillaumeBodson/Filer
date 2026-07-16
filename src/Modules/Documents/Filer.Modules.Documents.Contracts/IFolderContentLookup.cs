namespace Filer.Modules.Documents.Contracts;

/// <summary>
/// Cross-module read for AI analysis providers that inspect a candidate folder's
/// contents mid-analysis (#119, 06-ai-analysis-pipeline.md): a small sample of the
/// file names an owner keeps in one folder. A narrow contract owned by the module
/// that owns the rows, mirroring <c>IDocumentAnalysisGateway</c>
/// (10-solution-structure.md). Owner-scoped by construction: a cross-owner or
/// soft-deleted folder yields <b>empty</b>, indistinguishable from an empty folder
/// — the uniform-404 invariant applied to a read (05-security.md).
/// </summary>
public interface IFolderContentLookup
{
    /// <summary>
    /// File names of the owner's most recently added non-deleted documents in the
    /// folder, newest first, at most <paramref name="take"/> of them. Empty when
    /// the folder holds no documents for this owner — including when the folder
    /// belongs to someone else or is soft-deleted (its documents cascade-deleted
    /// with it, ADR-007).
    /// </summary>
    Task<IReadOnlyList<string>> GetFolderSampleAsync(
        Guid ownerId, Guid folderId, int take, CancellationToken cancellationToken);
}
