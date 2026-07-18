using Filer.ApiClient.Generated;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;
using Microsoft.Kiota.Abstractions;

namespace Filer.Ui.Search;

/// <summary>Default <see cref="ISearchService"/> over the generated client.</summary>
public sealed class SearchService(FilerApiClient api) : ISearchService
{
    private readonly FilerApiClient _api = api;

    public async Task<SearchPageResult> SearchAsync(
        SearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            PagedResultOfSearchHitResponse? page = await _api.Api.V1.Search.GetAsync(
                request =>
                {
                    request.QueryParameters.Q = query.Text;
                    request.QueryParameters.Page = query.Page;
                    request.QueryParameters.PageSize = query.PageSize;
                },
                cancellationToken).ConfigureAwait(false);

            return page is null
                ? new SearchPageResult(null, new ProblemDetailsView
                {
                    Title = "Search unavailable",
                    Detail = "The server returned an empty response. Try again.",
                })
                : new SearchPageResult(page, null);
        }
        catch (ApiException ex)
        {
            return new SearchPageResult(null, ex.ToProblemView());
        }
    }
}
