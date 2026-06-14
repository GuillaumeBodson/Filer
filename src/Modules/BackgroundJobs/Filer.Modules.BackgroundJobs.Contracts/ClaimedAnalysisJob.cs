namespace Filer.Modules.BackgroundJobs.Contracts;

/// <summary>
/// The slice of a claimed analysis job a handler needs to process it; the
/// <c>AnalysisJob</c> entity stays internal to the BackgroundJobs module
/// (10-solution-structure.md). <see cref="AttemptCount"/> is the attempt the
/// claim started (1-based) — the worker uses it against the configured attempt
/// limit to decide retry versus terminal failure (06-ai-analysis-pipeline.md).
/// </summary>
public sealed record ClaimedAnalysisJob(Guid JobId, Guid DocumentId, int AttemptCount);
