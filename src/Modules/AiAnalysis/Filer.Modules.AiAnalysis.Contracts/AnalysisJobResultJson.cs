using System.Text.Json;

namespace Filer.Modules.AiAnalysis.Contracts;

/// <summary>
/// The single serialization contract for <see cref="DocumentAnalysisResult"/> as
/// persisted in <c>AnalysisJob.Result</c> (JSONB, 02-data-model.md): the worker
/// writes with <see cref="Serialize"/> and every reader (the status and apply
/// slices, 06-ai-analysis-pipeline.md) parses with <see cref="Deserialize"/>, so
/// the shape can never drift between writer and readers. The options are exactly
/// <see cref="JsonSerializerDefaults.Web"/> — camelCase property names, enums as
/// numbers, no extra converters. Do not add converters or parallel serializers.
/// </summary>
public static class AnalysisJobResultJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes a result to the JSON persisted as <c>AnalysisJob.Result</c>.</summary>
    public static string Serialize(DocumentAnalysisResult result) =>
        JsonSerializer.Serialize(result, Options);

    /// <summary>Parses a persisted <c>AnalysisJob.Result</c> payload; null for the JSON literal <c>null</c>.</summary>
    public static DocumentAnalysisResult? Deserialize(string json) =>
        JsonSerializer.Deserialize<DocumentAnalysisResult>(json, Options);
}
