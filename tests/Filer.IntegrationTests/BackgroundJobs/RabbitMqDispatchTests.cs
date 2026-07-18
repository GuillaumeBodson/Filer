using System.Diagnostics;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Domain;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace Filer.IntegrationTests.BackgroundJobs;

/// <summary>
/// RabbitMQ dispatch end to end (ADR-008) against a real broker: enqueue commits
/// the row then publishes a wake-up; the consumer re-runs the shared claim, so
/// duplicates are no-ops; broker-down degrades to the sweeper with no work lost.
/// Runs inside the shared integration collection (sequential) because each test
/// boots a dedicated host through environment variables — the same mechanism the
/// shared factory uses (see <see cref="FilerApiFactory"/> remarks).
/// </summary>
/// <remarks>
/// The processed jobs reference documents that do not exist, so the handler
/// reports document-gone and the job lands on <c>Cancelled</c> — a terminal
/// state that proves the full dispatch → claim → handler → bookkeeping pipeline
/// ran (06: a deleted document is a cancellation, not a failure).
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class RabbitMqDispatchTests : IAsyncLifetime
{
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(30);

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public async ValueTask InitializeAsync() => await _rabbitMq.StartAsync();

    public async ValueTask DisposeAsync() => await _rabbitMq.DisposeAsync();

    [Fact]
    public async Task Enqueue_PublishesAWakeUpTheConsumerProcesses()
    {
        using var env = new RabbitMqEnvScope(
            hostName: _rabbitMq.Hostname,
            port: _rabbitMq.GetMappedPublicPort(5672),
            queueName: UniqueQueueName(),
            enabled: true,
            // The sweeper cannot plausibly fire inside the test window: whatever
            // completes the job below was message-driven.
            sweepInterval: "01:00:00");
        await using var factory = new WebApplicationFactory<Program>();

        // Let the sweeper's immediate first (empty) iteration pass and go to
        // sleep for an hour before any work exists.
        _ = factory.Server;
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Guid jobId;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
            Result<Guid> result =
                await queue.EnqueueAnalysisAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue();
            jobId = result.Value;
        }

        AnalysisJob job = await WaitForTerminalStateAsync(factory, jobId);

        job.Status.Should().Be(AnalysisJobStatus.Cancelled,
            "the consumer claimed and dispatched the job; its document does not exist, so it cancels (06)");
        job.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task DuplicateWakeUp_NeverRunsAJobTwice()
    {
        string queueName = UniqueQueueName();
        using var env = new RabbitMqEnvScope(
            hostName: _rabbitMq.Hostname,
            port: _rabbitMq.GetMappedPublicPort(5672),
            queueName: queueName,
            enabled: true,
            sweepInterval: "01:00:00");
        await using var factory = new WebApplicationFactory<Program>();
        _ = factory.Server;
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Guid jobId;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
            jobId = (await queue.EnqueueAnalysisAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).Value;
        }

        // Redeliver the same wake-up by hand: the message is a bare signal, so a
        // duplicate must be a harmless no-op claim (ADR-008).
        await PublishRawWakeUpAsync(queueName, jobId);

        AnalysisJob job = await WaitForTerminalStateAsync(factory, jobId);
        job.AttemptCount.Should().Be(1, "the second wake-up found nothing to claim");

        // The duplicate may still be in flight when the first completes; give it
        // time to be consumed, then confirm nothing re-ran.
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        AnalysisJob after = await LoadJobAsync(factory, jobId);
        after.AttemptCount.Should().Be(1);
        after.Status.Should().Be(AnalysisJobStatus.Cancelled);
    }

    [Fact]
    public async Task BrokerDown_EnqueueSucceedsAndTheSweeperProcesses()
    {
        using var env = new RabbitMqEnvScope(
            hostName: "localhost",
            port: 1, // nothing listens here — every publish/consume attempt fails
            queueName: UniqueQueueName(),
            enabled: true,
            sweepInterval: "00:00:01");
        await using var factory = new WebApplicationFactory<Program>();
        _ = factory.Server;

        Guid jobId;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
            Result<Guid> result =
                await queue.EnqueueAnalysisAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

            // The publish failed (no broker) but the row is committed — the
            // accepted job must not be failed retroactively (ADR-008).
            result.IsSuccess.Should().BeTrue();
            jobId = result.Value;
        }

        AnalysisJob job = await WaitForTerminalStateAsync(factory, jobId);
        job.Status.Should().Be(AnalysisJobStatus.Cancelled, "the sweeper recovered the job with no broker at all");
    }

    [Fact]
    public async Task Enqueue_PublishesTheJobIdWithTheCallersTraceparentHeader()
    {
        string queueName = UniqueQueueName();
        using var env = new RabbitMqEnvScope(
            hostName: _rabbitMq.Hostname,
            port: _rabbitMq.GetMappedPublicPort(5672),
            queueName: queueName,
            // Consumer and sweeper off: the message must still be in the queue
            // for raw inspection.
            enabled: false,
            sweepInterval: "01:00:00");
        await using var factory = new WebApplicationFactory<Program>();

        using var source = new ActivitySource("Filer.IntegrationTests.RabbitMq");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        Guid jobId;
        string traceparent;
        using (Activity activity = source.StartActivity("upload")!)
        {
            traceparent = activity.Id!;
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
            jobId = (await queue.EnqueueAnalysisAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).Value;
        }

        (string body, string? headerTraceparent) = await GetRawWakeUpAsync(queueName);

        body.Should().Be(jobId.ToString("D"), "the message is a bare job-ready signal carrying only the id (ADR-008)");
        headerTraceparent.Should().Be(traceparent,
            "the publish carries the caller's traceparent from day one (ADR-013, additive to the row)");
    }

    private static string UniqueQueueName() => "filer.it." + Guid.NewGuid().ToString("N");

    private async Task<IConnection> ConnectRawAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = _rabbitMq.Hostname,
            Port = _rabbitMq.GetMappedPublicPort(5672),
            UserName = "guest",
            Password = "guest",
        };
        return await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
    }

    private async Task PublishRawWakeUpAsync(string queueName, Guid jobId)
    {
        await using IConnection connection = await ConnectRawAsync();
        await using IChannel channel =
            await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await channel.QueueDeclareAsync(
            queueName, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: TestContext.Current.CancellationToken);
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            body: Encoding.UTF8.GetBytes(jobId.ToString("D")),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<(string Body, string? Traceparent)> GetRawWakeUpAsync(string queueName)
    {
        await using IConnection connection = await ConnectRawAsync();
        await using IChannel channel =
            await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        BasicGetResult? delivery =
            await channel.BasicGetAsync(queueName, autoAck: true, TestContext.Current.CancellationToken);

        delivery.Should().NotBeNull("the enqueue must have published exactly one wake-up");
        string body = Encoding.UTF8.GetString(delivery!.Body.ToArray());
        string? traceparent =
            delivery.BasicProperties.Headers?.TryGetValue("traceparent", out object? raw) == true
            && raw is byte[] bytes
                ? Encoding.UTF8.GetString(bytes)
                : null;
        return (body, traceparent);
    }

    private static async Task<AnalysisJob> WaitForTerminalStateAsync(
        WebApplicationFactory<Program> factory, Guid jobId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + ProcessingTimeout;
        AnalysisJob? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await LoadJobAsync(factory, jobId);
            if (last.Status is AnalysisJobStatus.Succeeded
                or AnalysisJobStatus.Failed
                or AnalysisJobStatus.Cancelled)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(
            $"Job {jobId} never reached a terminal state within {ProcessingTimeout}; last status: {last?.Status}.");
    }

    private static async Task<AnalysisJob> LoadJobAsync(WebApplicationFactory<Program> factory, Guid jobId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        return await db.AnalysisJobs.AsNoTracking()
            .SingleAsync(j => j.Id == jobId, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Points a dedicated host at RabbitMQ dispatch via environment variables (the
    /// configuration channel the host reads eagerly — see <see cref="FilerApiFactory"/>)
    /// and restores the previous values on dispose. Safe because the collection
    /// runs sequentially and the already-built shared host no longer reads them.
    /// </summary>
    private sealed class RabbitMqEnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = [];

        public RabbitMqEnvScope(string hostName, int port, string queueName, bool enabled, string sweepInterval)
        {
            Set("BackgroundJobs__Enabled", enabled ? "true" : "false");
            Set("BackgroundJobs__Queue", "RabbitMq");
            Set("BackgroundJobs__SweepInterval", sweepInterval);
            Set("BackgroundJobs__RabbitMq__HostName", hostName);
            Set("BackgroundJobs__RabbitMq__Port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set("BackgroundJobs__RabbitMq__UserName", "guest");
            Set("BackgroundJobs__RabbitMq__Password", "guest");
            Set("BackgroundJobs__RabbitMq__QueueName", queueName);
        }

        private void Set(string name, string? value)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in _previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
