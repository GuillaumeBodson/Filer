namespace Filer.Modules.BackgroundJobs.Contracts;

/// <summary>
/// Stable machine-readable error codes for job-side outcomes. These are internal
/// worker diagnostics recorded on the job row — they never reach a client
/// problem-details body, hence the <c>jobs.</c> namespace prefix that sets them
/// apart from the client-facing snake_case vocabulary (03-api-specification.md).
/// Callers and tests assert on these, never on human-readable messages.
/// </summary>
public static class BackgroundJobsErrorCodes
{
    /// <summary>A job operation was attempted without a document id.</summary>
    public const string DocumentIdRequired = "jobs.document_id_required";

    /// <summary>
    /// The document under analysis no longer exists (deleted before or while the
    /// job ran). A handler returning this tells the worker to mark the job
    /// Cancelled rather than Failed (06-ai-analysis-pipeline.md, Job Lifecycle).
    /// </summary>
    public const string DocumentGone = "jobs.document_gone";
}
