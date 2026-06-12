namespace Filer.Modules.AiAnalysis.Contracts;

/// <summary>
/// Abstraction over AI-assisted document analysis (06-ai-analysis-pipeline.md).
/// Concrete adapters (Ollama, OpenAI, …) live in the AiAnalysis module
/// implementation and are selected by configuration; consumers depend on this
/// interface only, never on a vendor SDK or a concrete provider
/// (10-solution-structure.md, rule 5).
/// </summary>
/// <remarks>
/// Failures at this seam are infrastructural, not business outcomes: implementations
/// throw (provider timeout, rate limit, malformed provider response) and the calling
/// worker translates them into the job's retry/backoff handling
/// (13-code-quality-and-design.md, "Result vs exceptions"; 06, Reliability).
/// Implementations must honour <paramref name="cancellationToken"/> mid-flight —
/// deleting a document cancels its in-flight analysis (06).
/// </remarks>
public interface IAIAnalysisProvider
{
    /// <summary>
    /// Analyses one document and returns advisory suggestions. The result is a
    /// provider-neutral shape; nothing is applied without user confirmation (06).
    /// </summary>
    Task<DocumentAnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken);
}
