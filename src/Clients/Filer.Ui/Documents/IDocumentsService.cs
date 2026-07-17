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
}

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
