using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.BackgroundJobs.Contracts;

namespace Filer.Modules.Documents.Features.GetDocumentAnalysis;

/// <summary>
/// The analysis status contract for <c>GET /api/v1/documents/{id}/analysis</c>
/// (03-api-specification.md): explicit DTOs, never the Contracts records — shapes
/// at the boundary are owned by the slice (13-code-quality-and-design.md).
/// <see cref="Status"/> is one of <see cref="StatusNone"/>, "Queued", "Running",
/// "Succeeded", "Failed"; suggestions are present only when succeeded. A failed or
/// cancelled analysis surfaces as unavailable — never the provider's error detail
/// (06-ai-analysis-pipeline.md, Failure Handling; 05-security.md).
/// </summary>
public sealed record DocumentAnalysisResponse(
    Guid DocumentId,
    string Status,
    Guid? JobId,
    DateTimeOffset? CompletedAt,
    DocumentAnalysisSuggestionsResponse? Suggestions)
{
    /// <summary>No analysis exists for the document (never queued, or cancelled).</summary>
    public const string StatusNone = "None";

    /// <summary>No job, or a cancelled one: analysis unavailable, nothing pending.</summary>
    public static DocumentAnalysisResponse Unavailable(Guid documentId) =>
        new(documentId, StatusNone, JobId: null, CompletedAt: null, Suggestions: null);

    /// <summary>A queued or running job: the status string, no suggestions yet.</summary>
    public static DocumentAnalysisResponse Pending(Guid documentId, AnalysisJobSnapshot job) =>
        new(documentId, job.Status.ToString(), job.JobId, CompletedAt: null, Suggestions: null);

    /// <summary>
    /// Terminal failure: status only — no internal error detail ever crosses this
    /// boundary (05-security.md). The document itself stays fully usable.
    /// </summary>
    public static DocumentAnalysisResponse Failed(Guid documentId, AnalysisJobSnapshot job) =>
        new(documentId, nameof(AnalysisJobState.Failed), job.JobId, job.CompletedAt, Suggestions: null);

    /// <summary>A successful run with its suggestions projected to response DTOs.</summary>
    public static DocumentAnalysisResponse Succeeded(
        Guid documentId, AnalysisJobSnapshot job, DocumentAnalysisResult result) =>
        new(
            documentId,
            nameof(AnalysisJobState.Succeeded),
            job.JobId,
            job.CompletedAt,
            DocumentAnalysisSuggestionsResponse.From(result));
}

/// <summary>
/// The advisory suggestions of a succeeded analysis, mirroring the provider-neutral
/// Contracts shapes as response DTOs (06-ai-analysis-pipeline.md, Capabilities).
/// </summary>
public sealed record DocumentAnalysisSuggestionsResponse(
    AnalysisFolderSuggestionResponse? SuggestedFolder,
    IReadOnlyList<AnalysisTagSuggestionResponse> SuggestedTags)
{
    /// <summary>
    /// The slice's single Contracts → DTO projection (13-code-quality-and-design.md:
    /// explicit projection mapping, owned by the slice).
    /// </summary>
    public static DocumentAnalysisSuggestionsResponse From(DocumentAnalysisResult result) =>
        new(
            result.SuggestedFolder is null
                ? null
                : new AnalysisFolderSuggestionResponse(
                    result.SuggestedFolder.ExistingFolderId,
                    result.SuggestedFolder.Name,
                    result.SuggestedFolder.Confidence),
            result.SuggestedTags
                .Select(tag => new AnalysisTagSuggestionResponse(tag.Name, tag.Confidence))
                .ToList());
}

/// <summary>A recommended folder: an existing one (by id) or a proposed new name.</summary>
public sealed record AnalysisFolderSuggestionResponse(Guid? ExistingFolderId, string Name, double Confidence);

/// <summary>A recommended tag, confirmable by name via the apply endpoint.</summary>
public sealed record AnalysisTagSuggestionResponse(string Name, double Confidence);
