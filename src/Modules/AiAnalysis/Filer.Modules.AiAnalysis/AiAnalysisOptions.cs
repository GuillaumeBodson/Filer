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

    /// <summary>
    /// Name of the local, no-egress provider: a typed-HttpClient adapter over a
    /// self-hosted Ollama runtime (#52). Document content never leaves the
    /// deployment, so it is the privacy-respecting choice for real use
    /// (06, Privacy &amp; Provider Selection; 05-security.md).
    /// </summary>
    public const string OllamaProviderName = "Ollama";

    /// <summary>Selects the <c>IAIAnalysisProvider</c> implementation.</summary>
    [Required]
    public string Provider { get; init; } = FakeProviderName;

    /// <summary>
    /// Tuning for the Ollama adapter, bound from <c>AiAnalysis:Ollama</c>. Only
    /// meaningful — and only validated — when <see cref="Provider"/> is
    /// <see cref="OllamaProviderName"/>; ignored otherwise.
    /// </summary>
    public OllamaOptions Ollama { get; init; } = new();
}

/// <summary>
/// Connection and prompt tuning for the local Ollama runtime, bound from
/// <c>AiAnalysis:Ollama</c>. Every option has a safe local-default, so the section
/// is optional; the endpoint lives with the worker only and never reaches clients
/// (05-security.md).
/// </summary>
public sealed class OllamaOptions
{
    /// <summary>Base address of the Ollama HTTP API (default the local runtime).</summary>
    public string BaseUrl { get; init; } = "http://localhost:11434";

    /// <summary>Model tag pulled into the runtime and used for inference.</summary>
    public string Model { get; init; } = "llama3.2:3b";

    /// <summary>
    /// Per-request timeout in seconds. Local inference is slow, and the first call
    /// after startup also pays a cold-load cost while the model is read into memory —
    /// which can exceed a couple of minutes on a modest host. The default is
    /// deliberately generous to absorb that; a breach throws so the worker retries (06).
    /// </summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>
    /// Upper bound on the document text placed in the prompt — a prompt-size guard,
    /// not a correctness limit; longer text is truncated (suggestions are advisory).
    /// </summary>
    public int MaxPromptChars { get; init; } = 8_000;
}
