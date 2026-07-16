namespace Filer.Modules.AiAnalysis.Contracts;

/// <summary>
/// Provider-neutral outcome of one analysis run (06-ai-analysis-pipeline.md,
/// Provider Abstraction). Everything here is advisory: the worker persists this
/// shape as <c>AnalysisJob.Result</c> JSONB (02-data-model.md), the status slice
/// returns it, and the apply slice applies only what the user confirms. It is the
/// single contract between those slices — do not invent parallel shapes.
/// </summary>
/// <remarks>
/// Duplicate detection is deliberately absent (#164): exact content-hash
/// duplicates are rejected at upload time with 409 (03-api-specification.md), so
/// an analysis run can never see one, and semantic near-duplicates are future
/// feature work (06). Reintroducing a field here later is additive — the
/// persisted JSONB contract (<see cref="AnalysisJobResultJson"/>) tolerates
/// absent fields.
/// </remarks>
/// <param name="SuggestedFolder">The recommended folder, or null when the provider has none.</param>
/// <param name="SuggestedTags">Zero or more recommended tags.</param>
public sealed record DocumentAnalysisResult(
    FolderSuggestion? SuggestedFolder,
    IReadOnlyList<TagSuggestion> SuggestedTags);

/// <summary>
/// A recommended folder: an existing one (echoed by id) or a proposed new one
/// (<see cref="ExistingFolderId"/> null — creation happens only at apply time, after
/// user confirmation).
/// </summary>
/// <param name="ExistingFolderId">Id from <see cref="DocumentAnalysisRequest.ExistingFolders"/>, or null for a proposed folder.</param>
/// <param name="Name">Display name of the recommended folder.</param>
/// <param name="Confidence">Provider confidence in the range [0, 1].</param>
public sealed record FolderSuggestion(Guid? ExistingFolderId, string Name, double Confidence);

/// <summary>A recommended tag; applying it records <c>Source = AiSuggested</c> (02-data-model.md).</summary>
/// <param name="Name">Tag name — an existing tag when it matches, otherwise a proposed one.</param>
/// <param name="Confidence">Provider confidence in the range [0, 1].</param>
public sealed record TagSuggestion(string Name, double Confidence);
