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
}

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
