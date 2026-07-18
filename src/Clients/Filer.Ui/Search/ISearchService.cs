using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;

namespace Filer.Ui.Search;

/// <summary>
/// Ranked full-text search for the UI (#142): calls go through the typed Kiota
/// client (ADR-011); failures surface as <see cref="ProblemDetailsView"/> for
/// the shared renderer. The backing search is opaque to the UI — hits carry an
/// opaque relevance <c>Score</c>, comparable only within one response, so a
/// future semantic sibling (RM-04) changes nothing here (03-api-specification.md).
/// </summary>
public interface ISearchService
{
    Task<SearchPageResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}

/// <summary>The search request: a required term; paging is 1-based (server default 20, max 100).</summary>
public sealed record SearchQuery(
    string Text,
    int Page = 1,
    int PageSize = 20);

/// <summary>Outcome of a search call: exactly one side is set.</summary>
public sealed record SearchPageResult(
    PagedResultOfSearchHitResponse? Page,
    ProblemDetailsView? Problem);
