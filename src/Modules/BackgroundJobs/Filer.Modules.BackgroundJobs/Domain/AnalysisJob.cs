using Filer.SharedKernel.Domain;

namespace Filer.Modules.BackgroundJobs.Domain;

/// <summary>
/// Tracks asynchronous AI processing of a document (02-data-model.md). The table
/// doubles as the V1 durable work queue: workers poll/claim rows with row locking
/// (06-ai-analysis-pipeline.md, Worker &amp; Queue). <see cref="DocumentId"/> is a
/// plain reference — cross-schema foreign keys are avoided so the module can be
/// extracted without untangling database constraints (10-solution-structure.md).
/// </summary>
public sealed class AnalysisJob : BaseEntity
{
    public Guid DocumentId { get; set; }

    public AnalysisJobStatus Status { get; set; } = AnalysisJobStatus.Queued;

    /// <summary>
    /// Which <c>IAIAnalysisProvider</c> ran the job (06): stamped by the claim on
    /// every attempt, so null only until the first run starts and always naming
    /// the provider of the latest attempt after that.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>Number of processing attempts, for retry/backoff (06).</summary>
    public int AttemptCount { get; set; }

    /// <summary>Last failure detail; never contains secrets (13-code-quality-and-design.md).</summary>
    public string? Error { get; set; }

    /// <summary>AI suggestions as JSONB; applied only after user confirmation (02, 06).</summary>
    public string? Result { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// When a retried job becomes claimable again — the exponential-backoff stamp
    /// the claim query respects (06, Reliability). Null = due immediately.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// W3C traceparent of the enqueueing request (ADR-013): stamped on insert,
    /// read on claim so the worker links its processing span back to the
    /// originating trace. Null when nothing was being traced at enqueue time.
    /// The row — never a broker message — is the authoritative carrier.
    /// </summary>
    public string? CorrelationContext { get; set; }
}
