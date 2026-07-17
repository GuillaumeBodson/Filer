using Filer.Ui.Documents;

namespace Filer.Ui.Tests.Documents;

/// <summary>Scriptable <see cref="IDocumentsService"/>; records every query.</summary>
internal sealed class FakeDocumentsService : IDocumentsService
{
    public Queue<DocumentsPageResult> Results { get; } = new();

    /// <summary>Returned when the queue is empty (steady-state result).</summary>
    public DocumentsPageResult? Default { get; set; }

    public List<DocumentsQuery> Queries { get; } = [];

    public DocumentUploadResult? UploadResult { get; set; }
    public List<(string FileName, string ContentType, long Length)> Uploads { get; } = [];

    public Queue<DocumentMetadataResult> MetadataResults { get; } = new();
    public List<Guid> MetadataCalls { get; } = [];

    public Task<DocumentsPageResult> ListAsync(
        DocumentsQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return Task.FromResult(Results.Count > 0
            ? Results.Dequeue()
            : Default ?? throw new InvalidOperationException("No scripted result left."));
    }

    public async Task<DocumentUploadResult> UploadAsync(
        DocumentUploadRequest upload, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, cancellationToken);
        Uploads.Add((upload.FileName, upload.ContentType, buffer.Length));
        return UploadResult ?? throw new InvalidOperationException("No scripted upload result.");
    }

    public Task<DocumentMetadataResult> GetMetadataAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        MetadataCalls.Add(documentId);
        return Task.FromResult(MetadataResults.Count > 0
            ? MetadataResults.Dequeue()
            : throw new InvalidOperationException("No scripted metadata result."));
    }
}
