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

    /// <summary>
    /// Name of the experimental two-pass Ollama variant (#119): ranks folder
    /// candidates, samples their contents through an owner-scoped lookup, then
    /// confirms. Strictly opt-in — the no-egress default for real use stays the
    /// plain <see cref="OllamaProviderName"/> adapter (06, Privacy &amp; Provider
    /// Selection); see the 09-decision-log.md note.
    /// </summary>
    public const string OllamaAgenticProviderName = "OllamaAgentic";

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
    /// <remarks>
    /// Bound to <see cref="ContextWindowTokens"/>: raising this without raising the
    /// window fails validation rather than silently overflowing the context.
    /// </remarks>
    public int MaxPromptChars { get; init; } = 8_000;

    /// <summary>
    /// Context window handed to the runtime as <c>options.num_ctx</c>.
    /// <para>
    /// Sent explicitly because the runtime default is small — 4096 tokens on
    /// Ollama — and overflowing it is a <b>silent</b> failure: no error, no log,
    /// just a truncated prompt and quietly worse suggestions (#254). A property of
    /// the request, not of the deployment, so the adapter must carry it.
    /// </para>
    /// <para>
    /// ⚠️ The window is not free: it sizes the KV cache, which occupies the same
    /// VRAM as the model on a host where that is already the binding constraint.
    /// Raise it because the prompt budget demands it, not for headroom's sake.
    /// </para>
    /// </summary>
    public int ContextWindowTokens { get; init; } = 8_192;

    /// <summary>
    /// Conservative characters-per-token divisor for sizing the window. English
    /// averages ~4; accented and non-Latin text tokenises worse, so 3 keeps the
    /// estimate on the safe side of a limit whose breach is silent.
    /// </summary>
    public const int EstimatedCharsPerToken = 3;

    /// <summary>
    /// Tokens reserved beyond the document text: the instruction preamble, the
    /// rendered folder tree with counts (#118), the existing tag list, and the
    /// model's own reply — all of which share the window with the text.
    /// </summary>
    public const int ContextHeadroomTokens = 2_048;

    /// <summary>
    /// Smallest <see cref="ContextWindowTokens"/> consistent with
    /// <see cref="MaxPromptChars"/>. Enforced at startup so the two cannot drift:
    /// the prompt budget and the window that must hold it are one decision made in
    /// two places.
    /// </summary>
    public int MinimumContextWindowTokens =>
        (MaxPromptChars / EstimatedCharsPerToken) + ContextHeadroomTokens;

    /// <summary>
    /// Whether the model may emit a reasoning chain before its answer.
    /// <para>
    /// Defaults to <c>false</c>, and that default is load-bearing. A hybrid-reasoning
    /// model (Qwen3, gpt-oss) reasons by default and produces up to ~1700 words of
    /// thinking before the JSON — output classification discards. Measured on the
    /// deployment node: <b>~3 s per document with reasoning off, 30–104 s with it on</b>.
    /// </para>
    /// <para>
    /// Set to <c>null</c> to omit the field from the request entirely, for a runtime
    /// that rejects it outright. <c>true</c> is available but has no known use here:
    /// the reply is a small fixed schema, not an argument.
    /// </para>
    /// </summary>
    public bool? Think { get; init; } = false;
}
