using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.Documents.Analysis;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.GetDocumentAnalysis;

/// <summary>
/// The analysis-status slice (#54, 06-ai-analysis-pipeline.md): resolve the
/// caller's document, then report the latest job — no job or cancelled is
/// "analysis unavailable", queued/running is pending, terminal failure surfaces as
/// a bare "Failed" with no provider detail (05-security.md), and success carries
/// the suggestions. Cross-owner, missing, and soft-deleted documents are a uniform
/// 404, never 403 (05-security.md).
/// </summary>
public sealed class GetDocumentAnalysisService(
    IDocumentStore documents,
    IAnalysisJobReader analysisJobs,
    ICurrentUser currentUser,
    ILogger<GetDocumentAnalysisService> logger)
{
    public async Task<Result<DocumentAnalysisResponse>> HandleAsync(
        Guid documentId, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filter below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<DocumentAnalysisResponse>(Error.Unauthorized());
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md). The job reader is not owner-scoped,
        // so this check MUST come first.
        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<DocumentAnalysisResponse>(
                Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound));
        }

        AnalysisJobSnapshot? job = await analysisJobs.FindLatestForDocumentAsync(
            documentId, cancellationToken);

        DocumentAnalysisResponse response = Project(documentId, job);

        logger.AnalysisStatusServed(documentId, currentUser.Id, response.Status);

        return Result.Success(response);
    }

    private DocumentAnalysisResponse Project(Guid documentId, AnalysisJobSnapshot? job)
    {
        // Never queued, or cancelled (e.g. superseded before processing):
        // analysis unavailable, indistinguishably (06, Job Lifecycle).
        if (job is null || job.Status == AnalysisJobState.Cancelled)
        {
            return DocumentAnalysisResponse.Unavailable(documentId);
        }

        switch (job.Status)
        {
            case AnalysisJobState.Queued:
            case AnalysisJobState.Running:
                return DocumentAnalysisResponse.Pending(documentId, job);

            // Terminal failure is surfaced as analysis-unavailable; the document
            // stays fully usable and no error detail leaks (06, Failure Handling).
            case AnalysisJobState.Failed:
                return DocumentAnalysisResponse.Failed(documentId, job);

            case AnalysisJobState.Succeeded:
                DocumentAnalysisResult? result = AnalysisResultJson.TryDeserialize(job.Result);
                if (result is null)
                {
                    // A succeeded job whose stored result is missing or unreadable
                    // should not happen; degrade to the unavailable shape rather
                    // than failing the request or leaking the inconsistency.
                    logger.AnalysisResultUnreadable(documentId, job.JobId);
                    return DocumentAnalysisResponse.Failed(documentId, job);
                }

                return DocumentAnalysisResponse.Succeeded(documentId, job, result);

            default:
                throw new InvalidOperationException($"Unmapped analysis job state '{job.Status}'.");
        }
    }
}

/// <summary>
/// Log messages for <see cref="GetDocumentAnalysisService"/>, co-located per the
/// house pattern: compile-time-generated and allocation-free via
/// <c>[LoggerMessage]</c>. Ids and statuses only — never suggestion content or
/// provider detail (05-security.md). Debug for the routine read; Warning for the
/// data inconsistency that demands operator attention.
/// </summary>
internal static partial class GetDocumentAnalysisServiceLog
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Document {DocumentId} analysis status '{Status}' served to owner {OwnerId}.")]
    public static partial void AnalysisStatusServed(
        this ILogger logger, Guid documentId, Guid ownerId, string status);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Analysis job {JobId} for document {DocumentId} succeeded but its stored result is missing or unreadable; surfaced as unavailable.")]
    public static partial void AnalysisResultUnreadable(this ILogger logger, Guid documentId, Guid jobId);
}
