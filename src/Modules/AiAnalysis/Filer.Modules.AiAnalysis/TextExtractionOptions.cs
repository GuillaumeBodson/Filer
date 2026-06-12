namespace Filer.Modules.AiAnalysis;

/// <summary>
/// Text-extraction configuration for the analysis job handler, bound from the
/// <c>AiAnalysis:TextExtraction</c> section. Every option has a safe default, so
/// the section is optional. Kept separate from <c>AiAnalysisOptions</c>: provider
/// selection and extraction tuning evolve independently (#52 owns the former).
/// </summary>
public sealed class TextExtractionOptions
{
    public const string SectionName = "AiAnalysis:TextExtraction";

    /// <summary>
    /// Upper bound on the characters extracted from a textual document and handed
    /// to the provider — a prompt-size guard, not a correctness limit; longer
    /// documents are truncated (06-ai-analysis-pipeline.md: suggestions are
    /// advisory, a prefix is enough signal).
    /// </summary>
    public int MaxChars { get; init; } = 8_000;
}
