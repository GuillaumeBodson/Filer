using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Filer.Modules.AiAnalysis.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filer.Modules.AiAnalysis;

/// <summary>
/// Local, no-egress <see cref="IAIAnalysisProvider"/>: an Adapter over a self-hosted
/// Ollama runtime (06-ai-analysis-pipeline.md, Privacy &amp; Provider Selection).
/// Document content is sent only to the configured local endpoint, so it never
/// leaves the deployment (05-security.md). The vendor wire shape never leaks inward:
/// the response is mapped to the provider-neutral <see cref="DocumentAnalysisResult"/>.
/// </summary>
/// <remarks>
/// <para>Calls Ollama's native <c>POST /api/chat</c> with <c>stream:false</c> and a
/// JSON <c>format</c> schema constraining the reply to the suggestion shape, then
/// maps it: a folder name that matches an existing folder (case-insensitively) is
/// echoed by id, an unknown name becomes a proposed folder, confidences are clamped
/// to [0,1], and tags are de-duplicated case-insensitively. Duplicate detection is
/// not the LLM's job in V1, so <see cref="DocumentAnalysisResult.DuplicateSignals"/>
/// is always empty.</para>
/// <para>This is an infrastructure seam, so failures throw (non-2xx, timeout,
/// unparseable reply) and the worker owns retry/backoff
/// (13-code-quality-and-design.md; <see cref="IAIAnalysisProvider"/> remarks).
/// Document text, prompts, and model replies are never placed in exception messages
/// or logs (05-security.md).</para>
/// </remarks>
public sealed class OllamaAnalysisProvider(
    HttpClient httpClient,
    IOptions<AiAnalysisOptions> options,
    ILogger<OllamaAnalysisProvider> logger) : IAIAnalysisProvider
{
    // Camel-case to match Ollama's native API; co-located so it is built once, not
    // per call (CA1869).
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // The JSON schema handed to Ollama's `format` field so the reply is structurally
    // constrained to the suggestion shape. Built once (CA1869).
    private static readonly JsonElement ResponseSchema = BuildResponseSchema();

    private readonly OllamaOptions _ollama = options.Value.Ollama;

    public async Task<DocumentAnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        long startTimestamp = Stopwatch.GetTimestamp();

        var chatRequest = new OllamaChatRequest(
            _ollama.Model,
            [new OllamaChatMessage("user", BuildPrompt(request))],
            Stream: false,
            ResponseSchema);

        DocumentAnalysisResult result;
        try
        {
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                "api/chat", chatRequest, SerializerOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.OllamaCallFailed(request.DocumentId, (int)response.StatusCode);
                throw new HttpRequestException(
                    $"Ollama chat request failed with status code {(int)response.StatusCode}.");
            }

            OllamaChatResponse reply = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                                           SerializerOptions, cancellationToken)
                                       ?? throw new InvalidOperationException("Ollama returned an empty chat response.");
            result = MapReply(reply, request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not HttpRequestException)
        {
            // Timeouts surface as OperationCanceledException; bad payloads as JSON or
            // mapping errors. Log without any content, then rethrow for the worker.
            logger.OllamaReplyUnusable(request.DocumentId);
            throw;
        }

        long elapsedMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        logger.AnalysisCompleted(
            request.DocumentId,
            elapsedMs,
            result.SuggestedFolder is not null,
            result.SuggestedTags.Count);

        return result;
    }

    /// <summary>
    /// Builds the instruction prompt from the request: file name, content type, the
    /// owner's existing folders/tags (so the model prefers them), and the document
    /// text truncated to <see cref="OllamaOptions.MaxPromptChars"/>. Folders render
    /// as the owner's tree with per-folder document counts (#118), so the model
    /// suggests into the existing organisation instead of inventing parallel
    /// folders. Works from the file name alone when
    /// <see cref="DocumentAnalysisRequest.Text"/> is empty (non-text documents —
    /// #53 only extracts text/plain and markdown).
    /// </summary>
    private string BuildPrompt(DocumentAnalysisRequest request)
    {
        string existingFolders = request.ExistingFolders.Count == 0
            ? "(none)"
            : RenderFolderTree(request.ExistingFolders);
        string existingTags = request.ExistingTags.Count == 0
            ? "(none)"
            : string.Join(", ", request.ExistingTags);
        string text = Truncate(request.Text, _ollama.MaxPromptChars);

        var prompt = new StringBuilder();
        prompt.Append("You organise a personal document library. Suggest one folder and relevant tags for the document below. ");
        prompt.Append("STRONGLY PREFER reusing one of the existing folders and existing tags; only invent a new name when none fit. ");
        prompt.Append("The existing folders form a tree; document counts show where the user actually files things. ");
        prompt.Append("Give each suggestion a confidence between 0 and 1. Reply only with the requested JSON.\n\n");
        prompt.Append("File name: ").Append(request.FileName).Append('\n');
        prompt.Append("Content type: ").Append(request.ContentType).Append('\n');
        prompt.Append("Existing folders: ").Append(existingFolders).Append('\n');
        prompt.Append("Existing tags: ").Append(existingTags).Append('\n');
        prompt.Append("Document text: ").Append(text.Length == 0 ? "(no extracted text)" : text);

        return prompt.ToString();
    }

    /// <summary>
    /// Renders the owner's folders as an indented tree with document counts, e.g.
    /// <c>- Work (3 documents)</c> with children two spaces deeper. Input arrives
    /// name-then-id ordered (IOwnerFolderReader), and grouping preserves that
    /// order, so the rendering is deterministic. A folder whose parent is not in
    /// the list is rendered as a root — defensive only; active folders always come
    /// with their active ancestors (soft-delete cascades, ADR-007).
    /// </summary>
    private static string RenderFolderTree(IReadOnlyList<ExistingFolder> folders)
    {
        ILookup<Guid, ExistingFolder> byParent = folders
            .Where(folder => folder.ParentId is not null)
            .ToLookup(folder => folder.ParentId!.Value);
        HashSet<Guid> knownIds = [.. folders.Select(folder => folder.Id)];

        var tree = new StringBuilder();
        foreach (ExistingFolder root in folders.Where(
                     folder => folder.ParentId is null || !knownIds.Contains(folder.ParentId.Value)))
        {
            AppendFolder(tree, byParent, root, depth: 0);
        }

        return tree.ToString();
    }

    private static void AppendFolder(
        StringBuilder tree, ILookup<Guid, ExistingFolder> byParent, ExistingFolder folder, int depth)
    {
        tree.Append('\n')
            .Append(' ', depth * 2)
            .Append("- ")
            .Append(folder.Name)
            .Append(" (")
            .Append(folder.DocumentCount)
            .Append(folder.DocumentCount == 1 ? " document)" : " documents)");

        foreach (ExistingFolder child in byParent[folder.Id])
        {
            AppendFolder(tree, byParent, child, depth + 1);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>Maps the model's structured reply to the provider-neutral result.</summary>
    private static DocumentAnalysisResult MapReply(OllamaChatResponse reply, DocumentAnalysisRequest request)
    {
        OllamaSuggestion suggestion = ParseSuggestion(reply.Message?.Content);

        FolderSuggestion? folder = MapFolder(suggestion.Folder, request.ExistingFolders);
        IReadOnlyList<TagSuggestion> tags = MapTags(suggestion.Tags);

        // Duplicate detection needs real content comparison, not the LLM (06, V1).
        return new DocumentAnalysisResult(folder, tags, []);
    }

    private static OllamaSuggestion ParseSuggestion(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Ollama returned a chat message with no content.");
        }

        return JsonSerializer.Deserialize<OllamaSuggestion>(content, SerializerOptions)
               ?? throw new InvalidOperationException("Ollama returned an unparseable suggestion payload.");
    }

    private static FolderSuggestion? MapFolder(
        OllamaFolderSuggestion? folder, IReadOnlyList<ExistingFolder> existingFolders)
    {
        if (folder is null || string.IsNullOrWhiteSpace(folder.Name))
        {
            return null;
        }

        string name = folder.Name.Trim();
        double confidence = Clamp(folder.Confidence);

        // Exact (case-insensitive) match resolves to the existing folder's id so
        // applying needs no name lookup; an unknown name is a proposed folder.
        ExistingFolder? match = existingFolders
            .FirstOrDefault(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));

        return match is null
            ? new FolderSuggestion(ExistingFolderId: null, name, confidence)
            : new FolderSuggestion(match.Id, match.Name, confidence);
    }

    private static List<TagSuggestion> MapTags(IReadOnlyList<OllamaTagSuggestion>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mapped = new List<TagSuggestion>(tags.Count);
        foreach (OllamaTagSuggestion tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
            {
                continue;
            }

            string name = tag.Name.Trim();
            if (seen.Add(name))
            {
                mapped.Add(new TagSuggestion(name, Clamp(tag.Confidence)));
            }
        }

        return mapped;
    }

    private static double Clamp(double confidence) => Math.Clamp(confidence, 0d, 1d);

    /// <summary>
    /// JSON schema for the <c>format</c> field: a folder name + confidence and an
    /// array of tag name + confidence pairs. Constrains the model to the shape this
    /// adapter maps from.
    /// </summary>
    private static JsonElement BuildResponseSchema()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "folder": {
              "type": "object",
              "properties": {
                "name": { "type": "string" },
                "confidence": { "type": "number" }
              },
              "required": ["name", "confidence"]
            },
            "tags": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "confidence": { "type": "number" }
                },
                "required": ["name", "confidence"]
              }
            }
          },
          "required": ["folder", "tags"]
        }
        """;

        using JsonDocument document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }

    // Ollama native /api/chat wire shapes. Internal to the adapter — they never
    // cross the provider seam (the Adapter pattern, 13).
    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OllamaChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] JsonElement Format);

    private sealed record OllamaChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaChatMessage? Message);

    // Decoded from the model's JSON content (constrained by ResponseSchema).
    private sealed record OllamaSuggestion(
        [property: JsonPropertyName("folder")] OllamaFolderSuggestion? Folder,
        [property: JsonPropertyName("tags")] IReadOnlyList<OllamaTagSuggestion>? Tags);

    private sealed record OllamaFolderSuggestion(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("confidence")] double Confidence);

    private sealed record OllamaTagSuggestion(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("confidence")] double Confidence);
}

/// <summary>
/// Log messages for <see cref="OllamaAnalysisProvider"/>, co-located per the house
/// convention (13-code-quality-and-design.md). Ids, status, and durations only —
/// never document text, prompts, or model replies (05-security.md).
/// </summary>
internal static partial class OllamaAnalysisProviderLog
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Ollama analysis completed for document {DocumentId} in {ElapsedMs} ms " +
                  "(folder suggested: {HasFolderSuggestion}, {TagCount} tag(s)).")]
    public static partial void AnalysisCompleted(
        this ILogger logger, Guid documentId, long elapsedMs, bool hasFolderSuggestion, int tagCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ollama chat request for document {DocumentId} failed with status code {StatusCode}; " +
                  "the job will be retried.")]
    public static partial void OllamaCallFailed(this ILogger logger, Guid documentId, int statusCode);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ollama reply for document {DocumentId} could not be used; the job will be retried.")]
    public static partial void OllamaReplyUnusable(this ILogger logger, Guid documentId);
}
