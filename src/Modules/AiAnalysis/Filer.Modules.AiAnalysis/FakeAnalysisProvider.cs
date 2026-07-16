using Filer.Modules.AiAnalysis.Contracts;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.AiAnalysis;

/// <summary>
/// Zero-footprint <see cref="IAIAnalysisProvider"/>: deterministic canned
/// suggestions derived from the request alone — no model, no network, no disk.
/// Lets every downstream slice (worker, status, apply) run end to end on machines
/// that cannot host a local LLM; the Ollama adapter (#52) provides real inference.
/// Determinism matters: re-running a job must produce a consistent result
/// (06-ai-analysis-pipeline.md, Reliability — idempotency).
/// </summary>
public sealed class FakeAnalysisProvider(ILogger<FakeAnalysisProvider> logger) : IAIAnalysisProvider
{
    internal const string ProposedFolderName = "Unsorted";
    internal const double ExistingMatchConfidence = 0.5;
    internal const double ProposedSuggestionConfidence = 0.3;
    private const int MaxTagSuggestions = 2;

    public Task<DocumentAnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FolderSuggestion folder = SuggestFolder(request);
        IReadOnlyList<TagSuggestion> tags = SuggestTags(request);

        logger.FakeSuggestionsProduced(request.DocumentId, folder.ExistingFolderId is not null, tags.Count);

        return Task.FromResult(new DocumentAnalysisResult(folder, tags));
    }

    /// <summary>Prefers the user's own organisation: first existing folder by name, else a proposed one.</summary>
    private static FolderSuggestion SuggestFolder(DocumentAnalysisRequest request)
    {
        ExistingFolder? first = request.ExistingFolders
            .OrderBy(folder => folder.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        return first is null
            ? new FolderSuggestion(ExistingFolderId: null, ProposedFolderName, ProposedSuggestionConfidence)
            : new FolderSuggestion(first.Id, first.Name, ExistingMatchConfidence);
    }

    /// <summary>Echoes up to two existing tags by name; with none, proposes the file extension.</summary>
    private static IReadOnlyList<TagSuggestion> SuggestTags(DocumentAnalysisRequest request)
    {
        if (request.ExistingTags.Count > 0)
        {
            return [.. request.ExistingTags
                .OrderBy(tag => tag, StringComparer.Ordinal)
                .Take(MaxTagSuggestions)
                .Select(tag => new TagSuggestion(tag, ExistingMatchConfidence))];
        }

        string extension = Path.GetExtension(request.FileName).TrimStart('.');

        return extension.Length == 0
            ? []
            : [new TagSuggestion(extension, ProposedSuggestionConfidence)];
    }
}

/// <summary>
/// Log messages for <see cref="FakeAnalysisProvider"/>, co-located per the house
/// convention (13-code-quality-and-design.md).
/// </summary>
internal static partial class FakeAnalysisProviderLog
{
    // Ids, booleans and counts only — never a folder or tag name (05-security.md).
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Fake analysis provider produced canned suggestions for document {DocumentId} " +
                  "(matched existing folder: {MatchedExistingFolder}, {TagCount} tag(s)).")]
    public static partial void FakeSuggestionsProduced(this ILogger logger, Guid documentId, bool matchedExistingFolder, int tagCount);
}
