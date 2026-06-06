namespace Filer.Modules.BackgroundJobs.Domain;

/// <summary>
/// Lifecycle states of an analysis job (02-data-model.md, 06-ai-analysis-pipeline.md):
/// Queued → Running → Succeeded | Failed; Queued → Cancelled. Stored as text.
/// </summary>
public enum AnalysisJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
