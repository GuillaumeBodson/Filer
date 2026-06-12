using System.Text.Json;
using Filer.Modules.AiAnalysis.Contracts;

namespace Filer.Modules.Documents.Analysis;

/// <summary>
/// The single reader of <c>AnalysisJob.Result</c> JSON inside the Documents
/// module, shared by the status (#54) and apply (#55) slices.
///
/// CRITICAL cross-branch contract: the worker (#53) serializes
/// <see cref="DocumentAnalysisResult"/> with exactly
/// <c>new JsonSerializerOptions(JsonSerializerDefaults.Web)</c> — no extra
/// converters, enums as numbers — so these options MUST stay identical to that
/// writer. At integration this helper is swapped for the shared
/// <c>AnalysisJobResultJson</c> helper #53 adds to AiAnalysis.Contracts.
/// </summary>
internal static class AnalysisResultJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Deserializes a stored result, or returns null when the payload is absent or
    /// unreadable — the slices treat that as "analysis unavailable" rather than
    /// throwing, since a malformed row must never take the document down
    /// (06-ai-analysis-pipeline.md, Failure Handling).
    /// </summary>
    public static DocumentAnalysisResult? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DocumentAnalysisResult>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
