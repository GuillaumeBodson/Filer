using Filer.Modules.Documents.Domain;

namespace Filer.Modules.Documents.Features.Upload;

/// <summary>
/// The created document's metadata plus the queued analysis job id — returned
/// immediately with 201; the upload never waits on AI (03-api-specification.md,
/// upload behavior; 06-ai-analysis-pipeline.md).
/// </summary>
public sealed record UploadDocumentResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string ContentHash,
    string Status,
    DateTimeOffset CreatedAt,
    Guid AnalysisJobId)
{
    /// <summary>
    /// The slice's single entity → DTO projection (13-code-quality-and-design.md:
    /// explicit constructor/projection mapping, owned by the slice). The job id
    /// comes from the enqueue step, not the entity.
    /// </summary>
    public static UploadDocumentResponse From(Document document, Guid analysisJobId) => new(
        document.Id,
        document.FileName,
        document.ContentType,
        document.SizeBytes,
        document.ContentHash,
        document.Status.ToString(),
        document.CreatedAt,
        analysisJobId);
}
