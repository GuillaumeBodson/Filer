using Filer.Ui.Documents;
using Filer.Ui.Models;

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

    public DocumentUpdateResult? NextUpdateResult { get; set; }
    public List<(Guid Id, DocumentUpdate Update)> Updates { get; } = [];

    public DocumentContentResult? NextDownloadResult { get; set; }
    public List<Guid> Downloads { get; } = [];

    public ProblemDetailsView? NextDeleteResult { get; set; }
    public List<Guid> Deletes { get; } = [];

    public Task<DocumentUpdateResult> UpdateAsync(
        Guid documentId, DocumentUpdate update, CancellationToken cancellationToken = default)
    {
        Updates.Add((documentId, update));
        return Task.FromResult(NextUpdateResult
            ?? throw new InvalidOperationException("No scripted update result."));
    }

    public Task<DocumentContentResult> DownloadAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Downloads.Add(documentId);
        return Task.FromResult(NextDownloadResult
            ?? throw new InvalidOperationException("No scripted download result."));
    }

    public Task<ProblemDetailsView?> DeleteAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Deletes.Add(documentId);
        return Task.FromResult(NextDeleteResult);
    }
}
