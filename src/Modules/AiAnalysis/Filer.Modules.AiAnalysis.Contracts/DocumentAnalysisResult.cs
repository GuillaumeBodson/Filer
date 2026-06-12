namespace Filer.Modules.AiAnalysis.Contracts;

/// <summary>
/// Provider-neutral outcome of one analysis run (06-ai-analysis-pipeline.md,
/// Provider Abstraction). Everything here is advisory: the worker persists this
/// shape as <c>AnalysisJob.Result</c> JSONB (02-data-model.md), the status slice
/// returns it, and the apply slice applies only what the user confirms. It is the
/// single contract between those slices — do not invent parallel shapes.
/// </summary>
/// <param name="SuggestedFolder">The recommended folder, or null when the provider has none.</param>
/// <param name="SuggestedTags">Zero or more recommended tags.</param>
/// <param name="DuplicateSignals">Possible duplicates of the analysed document.</param>
public sealed record DocumentAnalysisResult(
    FolderSuggestion? SuggestedFolder,
    IReadOnlyList<TagSuggestion> SuggestedTags,
    IReadOnlyList<DuplicateSignal> DuplicateSignals);

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

/// <summary>A possible duplicate of the analysed document (06, Capabilities).</summary>
/// <param name="DocumentId">The other document this one appears to duplicate.</param>
/// <param name="Kind">How the duplicate was detected.</param>
/// <param name="Confidence">Detection confidence in the range [0, 1]; exact hash matches use 1.</param>
public sealed record DuplicateSignal(Guid DocumentId, DuplicateKind Kind, double Confidence);

/// <summary>Detection mechanism behind a <see cref="DuplicateSignal"/>.</summary>
public enum DuplicateKind
{
    /// <summary>Byte-identical content (matching <c>ContentHash</c>, 02-data-model.md).</summary>
    ExactContent,

    /// <summary>Semantically similar content (reserved for the pgvector evolution, 06).</summary>
    SemanticNearDuplicate,
}
