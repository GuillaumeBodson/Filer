using Filer.Ui.Models;
using Filer.Ui.Search;

namespace Filer.Ui.Tests.Search;

/// <summary>Scriptable <see cref="ISearchService"/>; records every query.</summary>
internal sealed class FakeSearchService : ISearchService
{
    public Queue<SearchPageResult> Results { get; } = new();

    /// <summary>Returned when the queue is empty (steady-state result).</summary>
    public SearchPageResult? Default { get; set; }

    public List<SearchQuery> Queries { get; } = [];

    public Task<SearchPageResult> SearchAsync(
        SearchQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return Task.FromResult(Results.Count > 0
            ? Results.Dequeue()
            : Default ?? throw new InvalidOperationException("No scripted search result left."));
    }
}
