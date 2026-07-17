using Filer.Ui.Documents;

namespace Filer.Ui.Tests.Documents;

/// <summary>Scriptable <see cref="IDocumentsService"/>; records every query.</summary>
internal sealed class FakeDocumentsService : IDocumentsService
{
    public Queue<DocumentsPageResult> Results { get; } = new();

    /// <summary>Returned when the queue is empty (steady-state result).</summary>
    public DocumentsPageResult? Default { get; set; }

    public List<DocumentsQuery> Queries { get; } = [];

    public Task<DocumentsPageResult> ListAsync(
        DocumentsQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return Task.FromResult(Results.Count > 0
            ? Results.Dequeue()
            : Default ?? throw new InvalidOperationException("No scripted result left."));
    }
}
