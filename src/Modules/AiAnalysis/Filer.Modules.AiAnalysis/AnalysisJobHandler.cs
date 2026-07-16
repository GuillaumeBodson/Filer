using System.Net.Mime;
using System.Text;
using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Folders.Contracts;
using Filer.Modules.Storage.Contracts;
using Filer.Modules.Tags.Contracts;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filer.Modules.AiAnalysis;

/// <summary>
/// The real <see cref="IAnalysisJobHandler"/>: orchestrates one analysis run
/// (06-ai-analysis-pipeline.md). Loads the document's analysis context through the
/// owning modules' contracts, extracts text, calls the configured
/// <see cref="IAIAnalysisProvider"/>, marks the document Ready and returns the
/// serialized result for the worker to persist as <c>AnalysisJob.Result</c>.
/// Re-running is idempotent: the same inputs produce the same request, the result
/// is a single JSONB overwrite, and Ready stays Ready (06, Reliability).
/// </summary>
/// <remarks>
/// <para><b>V1 text extraction is deliberately minimal:</b> only <c>text/plain</c>
/// and <c>text/markdown</c> blobs are read (as UTF-8, truncated to
/// <see cref="TextExtractionOptions.MaxChars"/>); every other content type —
/// including PDF and the Office formats — passes <c>Text = string.Empty</c>, so
/// providers work from the file name alone. Real content extraction (PDF text
/// layer, OCR) is a later evolution behind this same seam.</para>
/// <para>Failure semantics (13-code-quality-and-design.md): a missing/deleted
/// document is the one expected outcome, returned as a failure carrying
/// <see cref="BackgroundJobsErrorCodes.DocumentGone"/> so the worker cancels the
/// job. Infrastructure failures (storage I/O, provider timeout) throw and
/// propagate — the worker owns retry/backoff.</para>
/// </remarks>
public sealed class AnalysisJobHandler(
    IDocumentAnalysisGateway documents,
    IOwnerFolderReader folders,
    IOwnerTagReader tags,
    IFileStorageProvider storage,
    IAIAnalysisProvider provider,
    IOptions<TextExtractionOptions> textExtractionOptions,
    ILogger<AnalysisJobHandler> logger) : IAnalysisJobHandler
{
    public async Task<Result<string?>> HandleAsync(ClaimedAnalysisJob job, CancellationToken cancellationToken)
    {
        AnalysisDocumentSnapshot? document = await documents.FindForAnalysisAsync(job.DocumentId, cancellationToken);
        if (document is null)
        {
            // Deleted (or never existed): cancel, don't fail (06, Job Lifecycle).
            logger.DocumentGone(job.DocumentId);
            return Result.Failure<string?>(Error.NotFound(
                "The document under analysis no longer exists.",
                BackgroundJobsErrorCodes.DocumentGone));
        }

        string text = await ExtractTextAsync(document, cancellationToken);

        // The owner's existing organisation, so providers can prefer it over
        // inventing names (06, Provider Abstraction). Folder hierarchy and per-folder
        // document counts make that context structural (#118); every read is scoped
        // to the document's owner, so no other user's organisation can enter the
        // prompt (05-security.md).
        IReadOnlyList<OwnerFolder> ownerFolders = await folders.ListActiveAsync(document.OwnerId, cancellationToken);
        IReadOnlyDictionary<Guid, int> documentCounts =
            await documents.CountActiveByFolderAsync(document.OwnerId, cancellationToken);
        IReadOnlyList<string> ownerTags = await tags.ListNamesAsync(document.OwnerId, cancellationToken);

        var request = new DocumentAnalysisRequest(
            document.DocumentId,
            document.FileName,
            document.ContentType,
            text,
            [.. ownerFolders.Select(folder => new ExistingFolder(
                folder.Id, folder.Name, folder.ParentId, documentCounts.GetValueOrDefault(folder.Id)))],
            ownerTags,
            document.OwnerId);

        // Provider failures throw and propagate: the worker translates them into
        // retry/backoff (IAIAnalysisProvider remarks; 06, Reliability).
        DocumentAnalysisResult analysis = await provider.AnalyzeAsync(request, cancellationToken);

        string result = AnalysisJobResultJson.Serialize(analysis);

        await documents.MarkReadyAsync(document.DocumentId, cancellationToken);

        logger.AnalysisCompleted(
            job.DocumentId,
            analysis.SuggestedFolder is not null,
            analysis.SuggestedTags.Count,
            analysis.DuplicateSignals.Count);

        return Result.Success<string?>(result);
    }

    /// <summary>
    /// V1 extraction: UTF-8 text for <c>text/plain</c>/<c>text/markdown</c>,
    /// truncated to the configured maximum; empty for everything else (see the
    /// class remarks for the deliberate limitation).
    /// </summary>
    private async Task<string> ExtractTextAsync(
        AnalysisDocumentSnapshot document, CancellationToken cancellationToken)
    {
        if (!IsExtractableText(document.ContentType))
        {
            return string.Empty;
        }

        await using Stream content = await storage.OpenReadAsync(document.StorageKey, cancellationToken);
        using var reader = new StreamReader(content, Encoding.UTF8);

        int maxChars = textExtractionOptions.Value.MaxChars;
        char[] buffer = new char[maxChars];
        int total = 0;
        while (total < maxChars)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(total, maxChars - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return new string(buffer, 0, total);
    }

    /// <summary>Media-type check, ignoring any parameters (e.g. <c>; charset=utf-8</c>).</summary>
    private static bool IsExtractableText(string contentType)
    {
        ReadOnlySpan<char> mediaType = contentType.AsSpan();
        int parameterSeparator = mediaType.IndexOf(';');
        if (parameterSeparator >= 0)
        {
            mediaType = mediaType[..parameterSeparator];
        }

        mediaType = mediaType.Trim();

        return mediaType.Equals(MediaTypeNames.Text.Plain, StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals(MediaTypeNames.Text.Markdown, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Log messages for <see cref="AnalysisJobHandler"/>, co-located per the house
/// convention (13-code-quality-and-design.md). Ids and counts only — never file
/// names or document content (05-security.md).
/// </summary>
internal static partial class AnalysisJobHandlerLog
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Analysis skipped for document {DocumentId}: it no longer exists; the job will be cancelled.")]
    public static partial void DocumentGone(this ILogger logger, Guid documentId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Analysis completed for document {DocumentId} " +
                  "(folder suggested: {HasFolderSuggestion}, {TagCount} tag(s), {DuplicateCount} duplicate signal(s)).")]
    public static partial void AnalysisCompleted(
        this ILogger logger, Guid documentId, bool hasFolderSuggestion, int tagCount, int duplicateCount);
}
