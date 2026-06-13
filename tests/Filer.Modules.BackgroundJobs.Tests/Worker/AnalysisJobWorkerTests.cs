using System.Diagnostics.Metrics;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Tests.TestSupport;
using Filer.Modules.BackgroundJobs.Worker;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Filer.Modules.BackgroundJobs.Tests.Worker;

/// <summary>
/// The worker's claim/dispatch orchestration against fake seams: a claimed job is
/// dispatched exactly once and its outcome recorded; failures retry with
/// exponential backoff until the attempt limit, then fail terminally; a deleted
/// document cancels the job; shutdown mid-flight releases the job so no work is
/// lost (06-ai-analysis-pipeline.md, 12-testing-strategy.md).
/// </summary>
public sealed class AnalysisJobWorkerTests
{
    private static readonly DateTimeOffset TestTime = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAnalysisJobStore _store = new();
    private readonly Mock<IAnalysisJobHandler> _handler = new();
    private readonly MutableClock _clock = new() { UtcNow = TestTime };

    private static ClaimedAnalysisJob NewJob(int attemptCount = 1) =>
        new(Guid.NewGuid(), Guid.NewGuid(), attemptCount);

    private AnalysisJobWorker CreateSut(BackgroundJobsOptions? options = null)
    {
        // Real DI scopes so the worker's scope-per-iteration behaviour is exercised.
        var services = new ServiceCollection();
        services.AddScoped<IAnalysisJobStore>(_ => _store);
        services.AddScoped<IAnalysisJobHandler>(_ => _handler.Object);
        services.AddMetrics();
        ServiceProvider provider = services.BuildServiceProvider();

        return new AnalysisJobWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new BackgroundJobsOptions()),
            _clock,
            new BackgroundJobsMetrics(provider.GetRequiredService<IMeterFactory>()),
            NullLogger<AnalysisJobWorker>.Instance);
    }

    [Fact]
    public async Task ProcessNextAsync_WhenQueueEmpty_ReturnsFalseWithoutDispatching()
    {
        bool processed = await CreateSut().ProcessNextAsync(CancellationToken.None);

        processed.Should().BeFalse();
        _handler.Verify(
            h => h.HandleAsync(It.IsAny<ClaimedAnalysisJob>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessNextAsync_WhenHandlerSucceeds_MarksJobSucceededWithTheResultPayload()
    {
        const string resultJson = """{"suggestedFolder":null,"suggestedTags":[],"duplicateSignals":[]}""";
        ClaimedAnalysisJob job = NewJob();
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<string?>(resultJson));

        bool processed = await CreateSut().ProcessNextAsync(CancellationToken.None);

        processed.Should().BeTrue();
        (Guid jobId, string? payload) = _store.Succeeded.Should().ContainSingle().Subject;
        jobId.Should().Be(job.JobId);
        payload.Should().Be(resultJson, "the worker persists the handler's serialized result verbatim");
        _store.Failed.Should().BeEmpty();
        _store.Retried.Should().BeEmpty();
        _store.Released.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextAsync_WhenHandlerFailsBeforeTheAttemptLimit_SchedulesARetryWithTheBaseDelay()
    {
        ClaimedAnalysisJob job = NewJob(attemptCount: 1);
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string?>(
                Error.Unexpected("provider unavailable", "ai.provider_unavailable")));

        var options = new BackgroundJobsOptions { MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromSeconds(10) };
        bool processed = await CreateSut(options).ProcessNextAsync(CancellationToken.None);

        processed.Should().BeTrue();
        (Guid jobId, string error, TimeSpan delay) = _store.Retried.Should().ContainSingle().Subject;
        jobId.Should().Be(job.JobId);
        error.Should().Contain("ai.provider_unavailable");
        delay.Should().Be(TimeSpan.FromSeconds(10), "the first retry waits the base delay (2^0)");
        _store.Failed.Should().BeEmpty("the job still has attempts left");
        _store.Succeeded.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextAsync_WhenALaterAttemptFails_BacksOffExponentially()
    {
        ClaimedAnalysisJob job = NewJob(attemptCount: 2);
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string?>(
                Error.Unexpected("provider unavailable", "ai.provider_unavailable")));

        var options = new BackgroundJobsOptions { MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromSeconds(10) };
        await CreateSut(options).ProcessNextAsync(CancellationToken.None);

        _store.Retried.Should().ContainSingle()
            .Which.Delay.Should().Be(TimeSpan.FromSeconds(20), "attempt 2 backs off base * 2^1");
    }

    [Fact]
    public async Task ProcessNextAsync_WhenTheAttemptLimitIsExhausted_MarksTheJobTerminallyFailed()
    {
        ClaimedAnalysisJob job = NewJob(attemptCount: 3);
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string?>(
                Error.Unexpected("provider unavailable", "ai.provider_unavailable")));

        var options = new BackgroundJobsOptions { MaxAttempts = 3 };
        await CreateSut(options).ProcessNextAsync(CancellationToken.None);

        (Guid jobId, string error) = _store.Failed.Should().ContainSingle().Subject;
        jobId.Should().Be(job.JobId);
        error.Should().Contain("ai.provider_unavailable");
        _store.Retried.Should().BeEmpty("the attempt limit is exhausted — Failed is terminal (06)");
    }

    [Fact]
    public async Task ProcessNextAsync_WhenHandlerThrowsBeforeTheAttemptLimit_SchedulesARetry()
    {
        ClaimedAnalysisJob job = NewJob(attemptCount: 1);
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider timeout"));

        // The worker survives a throwing handler: the attempt is recorded as a
        // retryable failure and the iteration still reports work done.
        bool processed = await CreateSut().ProcessNextAsync(CancellationToken.None);

        processed.Should().BeTrue();
        (Guid jobId, string error, TimeSpan _) = _store.Retried.Should().ContainSingle().Subject;
        jobId.Should().Be(job.JobId);
        error.Should().Contain("provider timeout");
        _store.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextAsync_WhenHandlerThrowsAtTheAttemptLimit_MarksTheJobFailed()
    {
        ClaimedAnalysisJob job = NewJob(attemptCount: 3);
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("handler bug"));

        await CreateSut(new BackgroundJobsOptions { MaxAttempts = 3 }).ProcessNextAsync(CancellationToken.None);

        (Guid jobId, string error) = _store.Failed.Should().ContainSingle().Subject;
        jobId.Should().Be(job.JobId);
        error.Should().Contain("handler bug");
        _store.Retried.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextAsync_AfterARetry_ASecondAttemptCanSucceed()
    {
        // The retry state machine end to end at the worker's level: attempt 1
        // fails and is requeued; the requeued claim (attempt 2) succeeds.
        Guid jobId = Guid.NewGuid();
        Guid documentId = Guid.NewGuid();
        var firstAttempt = new ClaimedAnalysisJob(jobId, documentId, AttemptCount: 1);
        var secondAttempt = new ClaimedAnalysisJob(jobId, documentId, AttemptCount: 2);
        _store.Enqueue(firstAttempt);
        _store.Enqueue(secondAttempt);
        _handler
            .Setup(h => h.HandleAsync(firstAttempt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string?>(Error.Unexpected("transient", "ai.transient")));
        _handler
            .Setup(h => h.HandleAsync(secondAttempt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<string?>("{}"));

        AnalysisJobWorker sut = CreateSut();
        await sut.ProcessNextAsync(CancellationToken.None);
        await sut.ProcessNextAsync(CancellationToken.None);

        _store.Retried.Should().ContainSingle().Which.JobId.Should().Be(jobId);
        _store.Succeeded.Should().ContainSingle().Which.JobId.Should().Be(jobId);
        _store.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextAsync_WhenTheDocumentIsGone_CancelsTheJobInsteadOfFailingIt()
    {
        ClaimedAnalysisJob job = NewJob();
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string?>(Error.NotFound(
                "The document under analysis no longer exists.",
                BackgroundJobsErrorCodes.DocumentGone)));

        await CreateSut().ProcessNextAsync(CancellationToken.None);

        _store.Cancelled.Should().ContainSingle().Which.Should().Be(job.JobId);
        _store.Failed.Should().BeEmpty("a deleted document is a cancellation, not a failure (06)");
        _store.Retried.Should().BeEmpty();
        _store.Succeeded.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextAsync_WhenCancelledMidFlight_ReleasesJobAndPropagates()
    {
        ClaimedAnalysisJob job = NewJob();
        _store.Enqueue(job);

        using var cts = new CancellationTokenSource();
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .Returns(async (ClaimedAnalysisJob _, CancellationToken ct) =>
            {
                // Simulate shutdown arriving while the handler is working.
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return Result.Success<string?>(null);
            });

        Func<Task> act = () => CreateSut().ProcessNextAsync(cts.Token);

        // Graceful shutdown loses no work: the claim is handed back, not completed.
        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Released.Should().ContainSingle().Which.Should().Be(job.JobId);
        _store.Succeeded.Should().BeEmpty();
        _store.Failed.Should().BeEmpty();
        _store.Retried.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextAsync_DispatchesEachClaimedJobExactlyOnce()
    {
        ClaimedAnalysisJob first = NewJob();
        ClaimedAnalysisJob second = NewJob();
        _store.Enqueue(first);
        _store.Enqueue(second);
        _handler
            .Setup(h => h.HandleAsync(It.IsAny<ClaimedAnalysisJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<string?>(null));

        AnalysisJobWorker sut = CreateSut();
        (await sut.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();
        (await sut.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();
        (await sut.ProcessNextAsync(CancellationToken.None)).Should().BeFalse();

        _handler.Verify(h => h.HandleAsync(first, It.IsAny<CancellationToken>()), Times.Once);
        _handler.Verify(h => h.HandleAsync(second, It.IsAny<CancellationToken>()), Times.Once);
        _store.Succeeded.Select(s => s.JobId).Should().BeEquivalentTo([first.JobId, second.JobId]);
    }

    [Fact]
    public async Task ProcessNextAsync_OnSuccess_EmitsTheSucceededCounterAndDuration()
    {
        ClaimedAnalysisJob job = NewJob();
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<string?>("{}"));

        long succeeded = 0;
        var durations = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == BackgroundJobsMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "filer.background_jobs.succeeded")
            {
                Interlocked.Add(ref succeeded, value);
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name == "filer.background_jobs.duration")
            {
                lock (durations)
                {
                    durations.Add(value);
                }
            }
        });
        listener.Start();

        await CreateSut().ProcessNextAsync(CancellationToken.None);

        Interlocked.Read(ref succeeded).Should().Be(1);
        durations.Should().ContainSingle().Which.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ReportQueueDepthIfDueAsync_SamplesAtMostOncePerInterval()
    {
        var options = new BackgroundJobsOptions { QueueDepthReportInterval = TimeSpan.FromMinutes(1) };
        AnalysisJobWorker sut = CreateSut(options);

        await sut.ReportQueueDepthIfDueAsync(CancellationToken.None);
        await sut.ReportQueueDepthIfDueAsync(CancellationToken.None);
        _store.CountQueuedCalls.Should().Be(1, "the second call falls inside the report interval");

        _clock.UtcNow = TestTime + TimeSpan.FromMinutes(2);
        await sut.ReportQueueDepthIfDueAsync(CancellationToken.None);
        _store.CountQueuedCalls.Should().Be(2, "the interval has elapsed");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_NeverClaims()
    {
        AnalysisJobWorker sut = CreateSut(new BackgroundJobsOptions { Enabled = false });

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        _store.ClaimCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenCancelled()
    {
        // Tight poll interval so the loop spins; cancellation must end it promptly.
        AnalysisJobWorker sut = CreateSut(new BackgroundJobsOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
        });

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        await sut.StopAsync(CancellationToken.None);

        // The worker polled while running and stopped when asked — no hang, no throw.
        _store.ClaimCount.Should().BeGreaterThan(0);
    }

    /// <summary>Settable <see cref="IClock"/> so backoff/report timing is deterministic (12-testing-strategy.md).</summary>
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
