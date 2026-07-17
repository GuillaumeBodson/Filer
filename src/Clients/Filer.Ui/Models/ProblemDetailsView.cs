using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filer.Ui.Models;

/// <summary>
/// Client-side view of an RFC 7807 problem-details response
/// (03-api-specification.md). This is a small, transport-agnostic UI shape - not a
/// server DTO shared into the client (ADR-011) - so the same renderer surfaces every
/// 4xx/5xx uniformly, including field-level validation errors.
/// </summary>
public sealed record ProblemDetailsView
{
    /// <summary>RFC 7807 <c>type</c> URI.</summary>
    public string? Type { get; init; }

    /// <summary>Short, human-readable summary of the problem — safe to headline.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// Machine-readable error code from the <c>code</c> extension member
    /// (03-api-specification.md, #169) — e.g. <c>invalid_credentials</c>. When UI
    /// logic needs to branch on a failure, it must key off this, never off
    /// <see cref="Title"/> or <see cref="Detail"/> (today's screens render problems
    /// verbatim and don't branch yet).
    /// </summary>
    public string? Code { get; init; }

    /// <summary>HTTP status code, when known.</summary>
    public int? Status { get; init; }

    /// <summary>Human-readable explanation specific to this occurrence.</summary>
    public string? Detail { get; init; }

    /// <summary>Field-level validation errors (field name → messages).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// A 404 means the resource is missing or not owned by the caller (ownership →
    /// 404, 05-security.md); the UI surfaces this as "not found", not as an error.
    /// </summary>
    public bool IsNotFound => Status == 404;

    /// <summary>Whether any field-level validation errors are present.</summary>
    public bool HasValidationErrors => Errors.Count > 0;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Builds a view for a bare status code when no problem-details body is available
    /// (e.g. an opaque transport failure).
    /// </summary>
    public static ProblemDetailsView ForStatus(int status, string? title = null, string? detail = null) =>
        new() { Status = status, Title = title, Detail = detail };

    /// <summary>
    /// Parses an RFC 7807 <c>application/problem+json</c> body. Returns <c>null</c> for
    /// null/blank/non-JSON input so callers can fall back to a status-only view.
    /// </summary>
    public static ProblemDetailsView? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<ProblemDetailsDto>(json, SerializerOptions);
            if (dto is null)
            {
                return null;
            }

            return new ProblemDetailsView
            {
                Type = dto.Type,
                Title = dto.Title,
                Code = dto.Code,
                Status = dto.Status,
                Detail = dto.Detail,
                Errors = Normalize(dto.Errors),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> Normalize(
        Dictionary<string, string[]>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return new();
        }

        return errors.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)(pair.Value ?? []),
            StringComparer.Ordinal);
    }

    private sealed record ProblemDetailsDto
    {
        public string? Type { get; init; }
        public string? Title { get; init; }
        public string? Code { get; init; }
        public int? Status { get; init; }
        public string? Detail { get; init; }

        [JsonPropertyName("errors")]
        public Dictionary<string, string[]>? Errors { get; init; }
    }
}
