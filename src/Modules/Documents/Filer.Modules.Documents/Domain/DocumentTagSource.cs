namespace Filer.Modules.Documents.Domain;

/// <summary>
/// Who created a document-tag association (02-data-model.md). Drives the replace
/// semantics: <see cref="User"/> rows are managed by the document-tag endpoints,
/// while <see cref="AiSuggested"/> rows come from the analysis pipeline
/// (06-ai-analysis-pipeline.md) and are preserved on replace — removed only by an
/// explicit DELETE, or promoted to <see cref="User"/> when re-added (ADR-009).
/// </summary>
public enum DocumentTagSource
{
    /// <summary>The owner attached the tag directly via the document-tag endpoints.</summary>
    User,

    /// <summary>The AI analysis pipeline suggested the tag; applied without user action.</summary>
    AiSuggested,
}
