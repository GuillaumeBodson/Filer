using System.Text.Json;
using Filer.Modules.AiAnalysis.Contracts;

namespace Filer.Modules.Documents.Analysis;

/// <summary>
/// The Documents-side reader of <c>AnalysisJob.Result</c> JSON, shared by the
/// status (#54) and apply (#55) slices.
///
/// The serialization shape is owned by <see cref="AnalysisJobResultJson"/> in
/// AiAnalysis.Contracts — the single contract the worker (#53) writes and every
/// reader parses, so it can never drift. This wrapper only adds the slices'
/// resilience: an absent or malformed payload becomes null ("analysis
/// unavailable") rather than throwing, since a bad row must never take the
/// document down (06-ai-analysis-pipeline.md, Failure Handling).
/// </summary>
internal static class AnalysisResultJson
{
    public static DocumentAnalysisResult? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return AnalysisJobResultJson.Deserialize(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
