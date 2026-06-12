using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Analysis;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Tags.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.ApplyDocumentAnalysis;

/// <summary>
/// The apply-suggestions slice (#55, 06-ai-analysis-pipeline.md, Applying
/// Suggestions): apply exactly the subset of the latest succeeded analysis the
/// user confirms — the folder, some/all/none of the tags. Applied tags become
/// <c>AiSuggested</c> association rows (02-data-model.md); already-associated
/// tags are a no-op, never an error or a demotion, so re-applying is idempotent.
/// The folder move and the tag rows commit in one transaction. Cross-owner,
/// missing, and soft-deleted documents — and cross-owner suggestion folders — are
/// a uniform 404, never 403 (05-security.md).
/// </summary>
public sealed class ApplyDocumentAnalysisService(
    IDocumentStore documents,
    IAnalysisJobReader analysisJobs,
    ITagNameResolver tagNames,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<ApplyDocumentAnalysisService> logger)
{
    public async Task<Result<ApplyDocumentAnalysisResponse>> HandleAsync(
        Guid documentId, ApplyDocumentAnalysisRequest request, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership checks below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<ApplyDocumentAnalysisResponse>(Error.Unauthorized());
        }

        Result validation = ApplyDocumentAnalysisValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<ApplyDocumentAnalysisResponse>(validation.Error!);
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md). The job reader is not owner-scoped,
        // so this check MUST come first.
        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<ApplyDocumentAnalysisResponse>(
                Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound));
        }

        // Only a succeeded analysis carries suggestions to apply. An unreadable
        // stored result is treated the same as no result: there is nothing to
        // apply, and the inconsistency is the status slice's to surface.
        AnalysisJobSnapshot? job = await analysisJobs.FindLatestForDocumentAsync(
            documentId, cancellationToken);
        DocumentAnalysisResult? analysis = job is { Status: AnalysisJobState.Succeeded }
            ? AnalysisResultJson.TryDeserialize(job.Result)
            : null;
        if (analysis is null)
        {
            return Result.Failure<ApplyDocumentAnalysisResponse>(Error.NotFound(
                "The document has no completed analysis to apply.",
                DocumentsErrorCodes.AnalysisNotFound));
        }

        // Confirmed names must each match a stored suggestion (case-insensitive):
        // this endpoint applies suggestions, it does not accept arbitrary tags.
        string[] confirmedNames = request.Tags!
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (confirmedNames.Any(name => !analysis.SuggestedTags
                .Any(suggestion => string.Equals(suggestion.Name, name, StringComparison.OrdinalIgnoreCase))))
        {
            return Result.Failure<ApplyDocumentAnalysisResponse>(Error.Validation(
                "One or more confirmed tags are not among the analysis suggestions.",
                DocumentsErrorCodes.TagNotSuggested));
        }

        Result<Guid?> folderTarget = await ResolveFolderTargetAsync(request, analysis, cancellationToken);
        if (folderTarget.IsFailure)
        {
            return Result.Failure<ApplyDocumentAnalysisResponse>(folderTarget.Error!);
        }

        // Resolve the confirmed names to the caller's tags. The association row
        // needs a tag id, and V1 has no cross-module tag creation: a suggestion
        // for a tag the user has not created yet cannot be applied here.
        IReadOnlyList<ResolvedTag> resolved = await tagNames.ResolveOwnedByNamesAsync(
            currentUser.Id, confirmedNames, cancellationToken);
        if (resolved.Count < confirmedNames.Length)
        {
            return Result.Failure<ApplyDocumentAnalysisResponse>(Error.Validation(
                "One or more confirmed tags do not exist yet. Create the tag first, then re-apply.",
                DocumentsErrorCodes.SuggestedTagNotCreated));
        }

        IReadOnlyList<DocumentTag> current = await documents.ListTagsForDocumentAsync(
            documentId, cancellationToken);

        DateTimeOffset now = clock.UtcNow;

        // An existing association — User or AiSuggested — is left untouched:
        // applying is idempotent and never demotes a User row (ADR-009).
        var associatedTagIds = current.Select(a => a.TagId).ToHashSet();
        List<DocumentTag> toInsert = resolved
            .Select(tag => tag.Id)
            .Distinct()
            .Where(tagId => !associatedTagIds.Contains(tagId))
            .Select(tagId => new DocumentTag
            {
                DocumentId = documentId,
                TagId = tagId,
                Source = DocumentTagSource.AiSuggested,
                CreatedAt = now,
            })
            .ToList();

        bool applyFolder = request.ApplyFolder;
        if (applyFolder)
        {
            document.FolderId = folderTarget.Value;
            document.UpdatedAt = now;
        }

        if (applyFolder || toInsert.Count > 0)
        {
            // One transaction for the whole confirmation: the move and the new
            // AiSuggested rows land together or not at all.
            await documents.ApplyAnalysisAsync(
                applyFolder ? document : null, toInsert, cancellationToken);
        }
        // Accepting none of the suggestions is a legitimate success with no write.

        logger.SuggestionsApplied(documentId, currentUser.Id, applyFolder, toInsert.Count);

        IReadOnlyList<DocumentTag> updated = await documents.ListTagsForDocumentAsync(
            documentId, cancellationToken);

        return Result.Success(ApplyDocumentAnalysisResponse.From(document, applyFolder, updated));
    }

    /// <summary>
    /// The folder half of the confirmation: nothing when not confirmed; otherwise
    /// the suggested existing folder, re-verified against the caller's ownership at
    /// apply time — the suggestion may have gone stale since the analysis ran.
    /// A proposed NEW folder is not applicable in V1 (no cross-module creation).
    /// </summary>
    private async Task<Result<Guid?>> ResolveFolderTargetAsync(
        ApplyDocumentAnalysisRequest request,
        DocumentAnalysisResult analysis,
        CancellationToken cancellationToken)
    {
        if (!request.ApplyFolder)
        {
            return Result.Success<Guid?>(null);
        }

        if (analysis.SuggestedFolder is null)
        {
            return Result.Failure<Guid?>(Error.Validation(
                "The analysis suggested no folder to apply.",
                DocumentsErrorCodes.FolderNotSuggested));
        }

        if (analysis.SuggestedFolder.ExistingFolderId is not Guid folderId)
        {
            return Result.Failure<Guid?>(Error.Validation(
                "The suggested folder does not exist yet; create it and move the document explicitly.",
                DocumentsErrorCodes.ProposedFolderNotSupported));
        }

        // Owner-scoped and soft-delete-aware like the move slice: a deleted or
        // cross-owner folder is indistinguishable from a missing one (05-security.md).
        if (!await documents.OwnedFolderExistsAsync(currentUser.Id, folderId, cancellationToken))
        {
            return Result.Failure<Guid?>(Error.NotFound(
                "The suggested folder was not found.",
                DocumentsErrorCodes.FolderNotFound));
        }

        return Result.Success<Guid?>(folderId);
    }
}

/// <summary>
/// Log messages for <see cref="ApplyDocumentAnalysisService"/>, co-located per the
/// house pattern: compile-time-generated and allocation-free via
/// <c>[LoggerMessage]</c>. Ids and counts only — never tag or folder names
/// (05-security.md). Information level: applying suggestions mutates the
/// document's organisation and is audit-worthy.
/// </summary>
internal static partial class ApplyDocumentAnalysisServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Document {DocumentId} analysis suggestions applied by owner {OwnerId} (folder: {FolderApplied}, tags inserted: {TagsInserted}).")]
    public static partial void SuggestionsApplied(
        this ILogger logger, Guid documentId, Guid ownerId, bool folderApplied, int tagsInserted);
}
