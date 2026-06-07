namespace Filer.Modules.Documents.Domain;

/// <summary>
/// Lifecycle of a document with respect to AI analysis (02-data-model.md).
/// Upload always lands at <see cref="Uploaded"/>; the background pipeline moves it
/// forward asynchronously (06-ai-analysis-pipeline.md) — never the upload request.
/// </summary>
public enum DocumentStatus
{
    Uploaded,
    Analyzing,
    Ready,
    Failed,
}
