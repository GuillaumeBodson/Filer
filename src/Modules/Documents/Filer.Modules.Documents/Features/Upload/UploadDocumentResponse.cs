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
    Guid AnalysisJobId);
