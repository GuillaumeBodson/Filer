namespace Filer.Modules.BackgroundJobs;

/// <summary>
/// Worker configuration bound from the <c>BackgroundJobs</c> section. All values
/// have safe defaults, so the section is optional.
/// </summary>
public sealed class BackgroundJobsOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>
    /// Whether the hosted worker processes jobs. Disabled in test hosts that
    /// exercise the queue and claim path deterministically (12-testing-strategy.md).
    /// Also gates the RabbitMQ consumer when <see cref="Queue"/> is
    /// <see cref="QueueDispatch.RabbitMq"/>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// How jobs are dispatched to the worker (ADR-008): <see cref="QueueDispatch.Db"/>
    /// polls the table; <see cref="QueueDispatch.RabbitMq"/> publishes a wake-up
    /// message after the row commits and consumes it, with polling retained as a
    /// sweeper fallback. The default is unchanged behaviour.
    /// </summary>
    public QueueDispatch Queue { get; init; } = QueueDispatch.Db;

    /// <summary>
    /// How long the worker sleeps when the queue is empty (Db dispatch).
    /// <para>
    /// Nothing signals the worker that a job arrived, so this is the dominant
    /// term in end-to-end analysis latency: a job waits for whatever remains of
    /// the current sleep — uniform over <c>[0, PollInterval]</c>, so a mean of
    /// half this value. Measured on the deployment node, inference itself takes
    /// ~2.1 s, which the previous 5 s default more than doubled (#281).
    /// </para>
    /// <para>
    /// The floor below which it is rejected is <see cref="MinPollInterval"/>;
    /// removing the wait altogether is what RabbitMQ dispatch is for (ADR-008).
    /// </para>
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Lower bound accepted for <see cref="PollInterval"/>. Each poll is a claim
    /// query against PostgreSQL; a mistyped value like <c>00:00:00.001</c> would
    /// spin the database for no latency gain the user could perceive.
    /// </summary>
    public static readonly TimeSpan MinPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Sweep cadence when RabbitMQ dispatch is active (ADR-008): the polling loop
    /// remains as the fallback that recovers work whose wake-up message was lost
    /// (broker outage), so it runs slowly instead of stopping.
    /// </summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Total processing attempts a job gets before a failure becomes terminal
    /// (06-ai-analysis-pipeline.md, Reliability — capped retries).
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay for retry backoff. Attempt <c>n</c> failing schedules the next
    /// try after <c>RetryBaseDelay * 2^(n-1)</c> (exponential, 06).
    /// </summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the worker samples and reports the queue depth (structured log +
    /// observable gauge, 04-non-functional.md).
    /// </summary>
    public TimeSpan QueueDepthReportInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Broker settings for <see cref="QueueDispatch.RabbitMq"/>. Credentials come
    /// from env/secret store in real environments (05-security.md); the defaults
    /// match the local dev container behind the compose <c>mq</c> profile.
    /// </summary>
    public RabbitMqOptions RabbitMq { get; init; } = new();

    public sealed class RabbitMqOptions
    {
        public string HostName { get; init; } = "localhost";

        public int Port { get; init; } = 5672;

        public string UserName { get; init; } = "guest";

        public string Password { get; init; } = "guest";

        /// <summary>Durable queue carrying the "job {id} ready" wake-up messages (ADR-008).</summary>
        public string QueueName { get; init; } = "filer.analysis-jobs";
    }
}

/// <summary>Job dispatch mechanism (ADR-008); selected by configuration like the storage provider (07).</summary>
public enum QueueDispatch
{
    /// <summary>Poll the AnalysisJobs table directly — the V1 default.</summary>
    Db,

    /// <summary>Publish/consume broker wake-ups; the table stays the outbox and job-state record.</summary>
    RabbitMq,
}
