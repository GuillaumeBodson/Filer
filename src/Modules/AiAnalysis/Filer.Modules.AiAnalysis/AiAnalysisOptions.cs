using System.ComponentModel.DataAnnotations;

namespace Filer.Modules.AiAnalysis;

/// <summary>
/// AI analysis configuration bound from the <c>AiAnalysis</c> section. The provider
/// is configuration-driven (06-ai-analysis-pipeline.md): deployments switch
/// providers without code changes, and no concrete provider leaks into domain code.
/// Every option has a safe default, so the section is optional.
/// </summary>
public sealed class AiAnalysisOptions
{
    public const string SectionName = "AiAnalysis";

    /// <summary>
    /// Name of the zero-footprint provider: deterministic canned suggestions, no
    /// model, no network. The dev/test choice on machines that cannot host a local
    /// LLM; the no-egress Ollama adapter (#52) becomes the privacy-respecting
    /// default for real use (06, Privacy &amp; Provider Selection).
    /// </summary>
    public const string FakeProviderName = "Fake";

    /// <summary>Selects the <c>IAIAnalysisProvider</c> implementation.</summary>
    [Required]
    public string Provider { get; init; } = FakeProviderName;
}
