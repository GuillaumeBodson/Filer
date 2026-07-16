using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.Documents.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filer.Modules.AiAnalysis;

/// <summary>
/// Experimental, opt-in two-pass variant of the no-egress Ollama adapter (#119):
/// pass one ranks up to <see cref="MaxCandidates"/> folder candidates from the
/// owner's tree, then a sample of each existing candidate's contents is fetched
/// through <see cref="IFolderContentLookup"/> (owner-scoped — a foreign or deleted
/// folder reads as empty, 05-security.md) and pass two confirms the final folder
/// against those samples. Everything stays inside this one adapter behind the
/// unchanged single-call <see cref="IAIAnalysisProvider"/> contract
/// (06-ai-analysis-pipeline.md, Provider Abstraction); see the 09-decision-log.md
/// note on providers pulling owner-scoped data mid-analysis.
/// </summary>
/// <remarks>
/// <para><b>Deliberately self-contained:</b> wire shapes, schemas, prompt building
/// and reply mapping are duplicated from <see cref="OllamaAnalysisProvider"/>
/// rather than shared, so this experiment is deletable without touching the
/// shipped adapter (the issue's containment requirement outweighs DRY here, 13).
/// Never the default: selected only via <c>AiAnalysis:Provider</c> =
/// <see cref="AiAnalysisOptions.OllamaAgenticProviderName"/>.</para>
/// <para>When no candidate resolves to an existing folder there is nothing to
/// inspect, so the second pass is skipped and the top candidate is proposed as-is.
/// Tag suggestions always come from pass one — the confirmation pass is about the
/// folder only. Failure semantics match the plain adapter: infrastructure errors
/// throw and the worker owns retry/backoff; document text, prompts, and model
/// replies never reach logs or exception messages (05-security.md).</para>
/// </remarks>
public sealed class OllamaAgenticAnalysisProvider(
    HttpClient httpClient,
    IFolderContentLookup folderContents,
    IOptions<AiAnalysisOptions> options,
    ILogger<OllamaAgenticAnalysisProvider> logger) : IAIAnalysisProvider
{
    /// <summary>Upper bound on candidates ranked in pass one and inspected in pass two.</summary>
    internal const int MaxCandidates = 3;

    /// <summary>How many file names are sampled per candidate folder.</summary>
    internal const int FolderSampleSize = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonElement CandidateSchema = ParseSchema("""
        {
          "type": "object",
          "properties": {
            "candidates": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "confidence": { "type": "number" }
                },
                "required": ["name", "confidence"]
              }
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
          "required": ["candidates", "tags"]
        }
        """);

    private static readonly JsonElement ConfirmationSchema = ParseSchema("""
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
            }
          },
          "required": ["folder"]
        }
        """);

    private readonly OllamaOptions _ollama = options.Value.Ollama;

    public async Task<DocumentAnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        long startTimestamp = Stopwatch.GetTimestamp();

        // Pass one: rank folder candidates from the tree and suggest tags.
        OllamaCandidateReply ranked = await ChatAsync<OllamaCandidateReply>(
            BuildCandidatePrompt(request), CandidateSchema, cancellationToken);

        List<TagSuggestion> tags = MapTags(ranked.Tags);
        List<RankedCandidate> candidates = ResolveCandidates(ranked.Candidates, request.ExistingFolders);
        List<RankedCandidate> inspectable = [.. candidates.Where(c => c.Existing is not null)];

        FolderSuggestion? folder;
        if (inspectable.Count == 0)
        {
            // Nothing to inspect: no second pass, the top candidate (if any) is a
            // plain proposal, exactly what the single-pass adapter would produce.
            folder = candidates.Count == 0
                ? null
                : new FolderSuggestion(ExistingFolderId: null, candidates[0].Name, candidates[0].Confidence);
            logger.AgenticConfirmationSkipped(request.DocumentId, candidates.Count);
        }
        else
        {
            // Pass two: sample each existing candidate's contents (owner-scoped by
            // construction — request.OwnerId keys every read) and confirm.
            var samples = new List<CandidateSample>(inspectable.Count);
            int sampledFileCount = 0;
            foreach (RankedCandidate candidate in inspectable)
            {
                IReadOnlyList<string> sample = await folderContents.GetFolderSampleAsync(
                    request.OwnerId, candidate.Existing!.Id, FolderSampleSize, cancellationToken);
                samples.Add(new CandidateSample(candidate, sample));
                sampledFileCount += sample.Count;
            }

            OllamaConfirmationReply confirmed = await ChatAsync<OllamaConfirmationReply>(
                BuildConfirmationPrompt(request, samples), ConfirmationSchema, cancellationToken);

            folder = MapFolder(confirmed.Folder, request.ExistingFolders);
            logger.AgenticConfirmationRan(request.DocumentId, inspectable.Count, sampledFileCount);
        }

        long elapsedMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        logger.AgenticAnalysisCompleted(request.DocumentId, elapsedMs, folder is not null, tags.Count);

        return new DocumentAnalysisResult(folder, tags);
    }

    /// <summary>One structurally-constrained chat round trip; throws on any infrastructure failure.</summary>
    private async Task<TReply> ChatAsync<TReply>(
        string prompt, JsonElement schema, CancellationToken cancellationToken)
    {
        var chatRequest = new OllamaChatRequest(
            _ollama.Model,
            [new OllamaChatMessage("user", prompt)],
            Stream: false,
            schema);

        try
        {
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                "api/chat", chatRequest, SerializerOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.AgenticCallFailed((int)response.StatusCode);
                throw new HttpRequestException(
                    $"Ollama chat request failed with status code {(int)response.StatusCode}.");
            }

            OllamaChatResponse reply = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                                           SerializerOptions, cancellationToken)
                                       ?? throw new InvalidOperationException("Ollama returned an empty chat response.");

            string? content = reply.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("Ollama returned a chat message with no content.");
            }

            return JsonSerializer.Deserialize<TReply>(content, SerializerOptions)
                   ?? throw new InvalidOperationException("Ollama returned an unparseable reply payload.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not HttpRequestException)
        {
            // Timeouts surface as OperationCanceledException; bad payloads as JSON
            // errors. Log without any content, then rethrow for the worker.
            logger.AgenticReplyUnusable();
            throw;
        }
    }

    /// <summary>
    /// Pass-one prompt: the same document facts and folder tree as the plain
    /// adapter, but asking for a ranked candidate list instead of one folder.
    /// </summary>
    private string BuildCandidatePrompt(DocumentAnalysisRequest request)
    {
        string existingFolders = request.ExistingFolders.Count == 0
            ? "(none)"
            : RenderFolderTree(request.ExistingFolders);
        string existingTags = request.ExistingTags.Count == 0
            ? "(none)"
            : string.Join(", ", request.ExistingTags);
        string text = Truncate(request.Text, _ollama.MaxPromptChars);

        var prompt = new StringBuilder();
        prompt.Append("You organise a personal document library. Rank up to ")
            .Append(MaxCandidates)
            .Append(" candidate folders (best first) and suggest relevant tags for the document below. ");
        prompt.Append("STRONGLY PREFER reusing existing folders and existing tags; only invent a new name when none fit. ");
        prompt.Append("The existing folders form a tree; document counts show where the user actually files things. ");
        prompt.Append("Give each candidate and tag a confidence between 0 and 1. Reply only with the requested JSON.\n\n");
        prompt.Append("File name: ").Append(request.FileName).Append('\n');
        prompt.Append("Content type: ").Append(request.ContentType).Append('\n');
        prompt.Append("Existing folders: ").Append(existingFolders).Append('\n');
        prompt.Append("Existing tags: ").Append(existingTags).Append('\n');
        prompt.Append("Document text: ").Append(text.Length == 0 ? "(no extracted text)" : text);

        return prompt.ToString();
    }

    /// <summary>
    /// Pass-two prompt: the candidates with a sample of what the user already keeps
    /// in each, so the model confirms against real contents rather than names alone.
    /// </summary>
    private string BuildConfirmationPrompt(
        DocumentAnalysisRequest request, IReadOnlyList<CandidateSample> samples)
    {
        string text = Truncate(request.Text, _ollama.MaxPromptChars);

        var prompt = new StringBuilder();
        prompt.Append("You organise a personal document library. Pick the single best folder for the document below. ");
        prompt.Append("Each candidate folder lists a sample of the files the user already keeps there; ");
        prompt.Append("choose the candidate whose contents this document fits best, or a new folder name only if none fit. ");
        prompt.Append("Give the choice a confidence between 0 and 1. Reply only with the requested JSON.\n\n");
        prompt.Append("File name: ").Append(request.FileName).Append('\n');
        prompt.Append("Content type: ").Append(request.ContentType).Append('\n');
        prompt.Append("Candidate folders:");
        foreach (CandidateSample sample in samples)
        {
            prompt.Append("\n- ").Append(sample.Candidate.Name).Append(": ");
            prompt.Append(sample.FileNames.Count == 0
                ? "(no documents yet)"
                : string.Join(", ", sample.FileNames));
        }

        prompt.Append('\n');
        prompt.Append("Document text: ").Append(text.Length == 0 ? "(no extracted text)" : text);

        return prompt.ToString();
    }

    /// <summary>
    /// Maps the model's ranked names onto the owner's folders: a case-insensitive
    /// match becomes an inspectable candidate, an unknown name a proposable one;
    /// blanks and duplicates drop, model order (the ranking) is preserved.
    /// </summary>
    private static List<RankedCandidate> ResolveCandidates(
        IReadOnlyList<OllamaRankedFolder>? candidates, IReadOnlyList<ExistingFolder> existingFolders)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<RankedCandidate>(Math.Min(candidates.Count, MaxCandidates));
        foreach (OllamaRankedFolder candidate in candidates)
        {
            if (resolved.Count == MaxCandidates)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(candidate.Name))
            {
                continue;
            }

            string name = candidate.Name.Trim();
            if (!seen.Add(name))
            {
                continue;
            }

            ExistingFolder? existing = existingFolders.FirstOrDefault(
                folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));

            resolved.Add(new RankedCandidate(
                existing?.Name ?? name, Clamp(candidate.Confidence), existing));
        }

        return resolved;
    }

    private static FolderSuggestion? MapFolder(
        OllamaRankedFolder? folder, IReadOnlyList<ExistingFolder> existingFolders)
    {
        if (folder is null || string.IsNullOrWhiteSpace(folder.Name))
        {
            return null;
        }

        string name = folder.Name.Trim();
        double confidence = Clamp(folder.Confidence);

        ExistingFolder? match = existingFolders.FirstOrDefault(
            existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));

        return match is null
            ? new FolderSuggestion(ExistingFolderId: null, name, confidence)
            : new FolderSuggestion(match.Id, match.Name, confidence);
    }

    private static List<TagSuggestion> MapTags(IReadOnlyList<OllamaRankedTag>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mapped = new List<TagSuggestion>(tags.Count);
        foreach (OllamaRankedTag tag in tags)
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

    private static double Clamp(double confidence) => Math.Clamp(confidence, 0d, 1d);

    private static JsonElement ParseSchema(string schema)
    {
        using JsonDocument document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }

    /// <summary>A pass-one candidate resolved against the owner's folders.</summary>
    private sealed record RankedCandidate(string Name, double Confidence, ExistingFolder? Existing);

    /// <summary>An inspectable candidate and the sampled file names of its folder.</summary>
    private sealed record CandidateSample(RankedCandidate Candidate, IReadOnlyList<string> FileNames);

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

    // Decoded from the model's JSON content (constrained by the schemas above).
    private sealed record OllamaCandidateReply(
        [property: JsonPropertyName("candidates")] IReadOnlyList<OllamaRankedFolder>? Candidates,
        [property: JsonPropertyName("tags")] IReadOnlyList<OllamaRankedTag>? Tags);

    private sealed record OllamaConfirmationReply(
        [property: JsonPropertyName("folder")] OllamaRankedFolder? Folder);

    private sealed record OllamaRankedFolder(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("confidence")] double Confidence);

    private sealed record OllamaRankedTag(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("confidence")] double Confidence);
}

/// <summary>
/// Log messages for <see cref="OllamaAgenticAnalysisProvider"/>, co-located per the
/// house convention (13-code-quality-and-design.md). Ids, counts, status, and
/// durations only — never document text, file names, prompts, or model replies
/// (05-security.md).
/// </summary>
internal static partial class OllamaAgenticAnalysisProviderLog
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Agentic analysis completed for document {DocumentId} in {ElapsedMs} ms " +
                  "(folder suggested: {HasFolderSuggestion}, {TagCount} tag(s)).")]
    public static partial void AgenticAnalysisCompleted(
        this ILogger logger, Guid documentId, long elapsedMs, bool hasFolderSuggestion, int tagCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Agentic confirmation ran for document {DocumentId}: {CandidateCount} existing candidate(s) " +
                  "inspected, {SampledFileCount} file name(s) sampled.")]
    public static partial void AgenticConfirmationRan(
        this ILogger logger, Guid documentId, int candidateCount, int sampledFileCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Agentic confirmation skipped for document {DocumentId}: none of the {CandidateCount} " +
                  "candidate(s) matches an existing folder, so there is nothing to inspect.")]
    public static partial void AgenticConfirmationSkipped(
        this ILogger logger, Guid documentId, int candidateCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ollama agentic chat request failed with status code {StatusCode}; the job will be retried.")]
    public static partial void AgenticCallFailed(this ILogger logger, int statusCode);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ollama agentic reply could not be used; the job will be retried.")]
    public static partial void AgenticReplyUnusable(this ILogger logger);
}
