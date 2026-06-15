namespace Filer.Modules.Documents.Features.ApplyDocumentAnalysis;

/// <summary>
/// The POST body for <c>/documents/{id}/analysis/apply</c>: the subset of the
/// stored suggestions the user confirms (06-ai-analysis-pipeline.md, Applying
/// Suggestions — all, some, or none). <see cref="ApplyFolder"/> true applies the
/// suggested folder; <see cref="Tags"/> lists the confirmed suggested tag names
/// (matched case-insensitively against the suggestions). An empty list accepts no
/// tags and is legitimate; a null <see cref="Tags"/> is malformed (400), mirroring
/// the replace-tags body. Duplicate names are deduplicated rather than rejected.
/// </summary>
public sealed record ApplyDocumentAnalysisRequest(bool ApplyFolder, IReadOnlyList<string>? Tags);
