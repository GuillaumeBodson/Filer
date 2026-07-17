using Filer.ApiClient.Generated;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;
using Microsoft.Kiota.Abstractions;

namespace Filer.Ui.Documents;

/// <summary>Default <see cref="IDocumentsService"/> over the generated client.</summary>
public sealed class DocumentsService(FilerApiClient api) : IDocumentsService
{
    private readonly FilerApiClient _api = api;

    public async Task<DocumentsPageResult> ListAsync(
        DocumentsQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            PagedResultOfDocumentListItemResponse? page = await _api.Api.V1.Documents.GetAsync(
                request =>
                {
                    request.QueryParameters.FolderId = query.FolderId;
                    request.QueryParameters.TagId = query.TagId;
                    request.QueryParameters.Q = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text;
                    request.QueryParameters.Page = query.Page;
                    request.QueryParameters.PageSize = query.PageSize;
                },
                cancellationToken).ConfigureAwait(false);

            return page is null
                ? new DocumentsPageResult(null, new ProblemDetailsView
                {
                    Title = "Documents unavailable",
                    Detail = "The server returned an empty response. Try again.",
                })
                : new DocumentsPageResult(page, null);
        }
        catch (ApiException ex)
        {
            return new DocumentsPageResult(null, ex.ToProblemView());
        }
    }
}
