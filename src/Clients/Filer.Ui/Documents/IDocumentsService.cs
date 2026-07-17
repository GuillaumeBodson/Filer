using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;

namespace Filer.Ui.Documents;

/// <summary>
/// Document listing for the UI (#135): calls go through the typed Kiota client
/// (ADR-011); failures surface as <see cref="ProblemDetailsView"/> for the shared
/// renderer. Filters compose exactly like the API's query string (03-api-specification.md).
/// </summary>
public interface IDocumentsService
{
    Task<DocumentsPageResult> ListAsync(DocumentsQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads one file (multipart). Returns immediately - analysis runs in the
    /// background (06-ai-analysis-pipeline.md); poll <see cref="GetMetadataAsync"/>
    /// to observe the Uploaded → Ready transition.
    /// </summary>
    Task<DocumentUploadResult> UploadAsync(DocumentUploadRequest upload, CancellationToken cancellationToken = default);

    /// <summary>Loads one document's metadata (status polling, detail view #137).</summary>
    Task<DocumentMetadataResult> GetMetadataAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames and/or moves a document (merge-patch, 03-api-specification.md):
    /// only the fields the update carries change; moving with a null target goes
    /// to the root.
    /// </summary>
    Task<DocumentUpdateResult> UpdateAsync(Guid documentId, DocumentUpdate update, CancellationToken cancellationToken = default);

    /// <summary>Downloads the binary content (07-storage-and-deployment.md).</summary>
    Task<DocumentContentResult> DownloadAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the document. Returns <c>null</c> on success.</summary>
    Task<ProblemDetailsView?> DeleteAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>The document's current tag associations with their Source (#139, 02-data-model.md).</summary>
    Task<DocumentTagsResult> GetTagsAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Attaches an owned tag as <c>Source=User</c> (promotes an AI suggestion; idempotent).</summary>
    Task<DocumentTagsResult> AddTagAsync(Guid documentId, Guid tagId, CancellationToken cancellationToken = default);

    /// <summary>Detaches a tag from the document. Returns <c>null</c> on success.</summary>
    Task<ProblemDetailsView?> RemoveTagAsync(Guid documentId, Guid tagId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a tag read/mutation: exactly one side is set.</summary>
public sealed record DocumentTagsResult(
    DocumentTagsResponse? Tags,
    ProblemDetailsView? Problem);

/// <summary>What to change on a document; leave a member unset to keep it.</summary>
public sealed record DocumentUpdate
{
    /// <summary>New file name, or <c>null</c> to keep the current one.</summary>
    public string? NewFileName { get; init; }

    /// <summary>Whether the update changes the folder at all.</summary>
    public bool MoveFolder { get; init; }

    /// <summary>Target folder when <see cref="MoveFolder"/> is set; <c>null</c> = root.</summary>
    public Guid? TargetFolderId { get; init; }
}

/// <summary>Outcome of an update: exactly one side is set.</summary>
public sealed record DocumentUpdateResult(
    UpdateDocumentMetadataResponse? Document,
    ProblemDetailsView? Problem);

/// <summary>Downloaded bytes, or the problem that prevented the download.</summary>
public sealed record DocumentContentResult(
    byte[]? Content,
    ProblemDetailsView? Problem);

/// <summary>One file to upload; the stream is read once by the transport.</summary>
public sealed record DocumentUploadRequest(Stream Content, string FileName, string ContentType);

/// <summary>
/// Outcome of an upload. On a duplicate-content 409 the problem carries the existing
/// document's reference (<see cref="DuplicateOfDocumentId"/>, 03-api-specification.md).
/// </summary>
public sealed record DocumentUploadResult(
    UploadDocumentResponse? Document,
    ProblemDetailsView? Problem,
    Guid? DuplicateOfDocumentId = null);

/// <summary>Outcome of a metadata load: exactly one side is set.</summary>
public sealed record DocumentMetadataResult(
    DocumentMetadataResponse? Document,
    ProblemDetailsView? Problem);

/// <summary>The list request: filters combine; paging is 1-based (server default 20, max 100).</summary>
public sealed record DocumentsQuery(
    Guid? FolderId = null,
    Guid? TagId = null,
    string? Text = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>Outcome of a list call: exactly one side is set.</summary>
public sealed record DocumentsPageResult(
    PagedResultOfDocumentListItemResponse? Page,
    ProblemDetailsView? Problem);
