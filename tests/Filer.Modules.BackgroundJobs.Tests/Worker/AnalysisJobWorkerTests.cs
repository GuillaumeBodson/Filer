using Filer.Modules.BackgroundJobs.Tests.TestSupport;
using Filer.Modules.BackgroundJobs.Worker;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Filer.Modules.BackgroundJobs.Tests.Worker;

/// <summary>
/// The worker's claim/dispatch orchestration against fake seams: a claimed job is
/// dispatched exactly once and its outcome recorded; shutdown mid-flight releases
/// the job so no work is lost (06-ai-analysis-pipeline.md, 12-testing-strategy.md).
/// </summary>
public sealed class AnalysisJobWorkerTests
{
    private readonly FakeAnalysisJobStore _store = new();
    private readonly Mock<IAnalysisJobHandler> _handler = new();

    private static ClaimedAnalysisJob NewJob() => new(Guid.NewGuid(), Guid.NewGuid(), AttemptCount: 1);

    private AnalysisJobWorker CreateSut(BackgroundJobsOptions? options = null)
    {
        // Real DI scopes so the worker's scope-per-iteration behaviour is exercised.
        var services = new ServiceCollection();
        services.AddScoped<IAnalysisJobStore>(_ => _store);
        services.AddScoped<IAnalysisJobHandler>(_ => _handler.Object);
        ServiceProvider provider = services.BuildServiceProvider();

        return new AnalysisJobWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new BackgroundJobsOptions()),
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
    public async Task ProcessNextAsync_WhenHandlerSucceeds_MarksJobSucceeded()
    {
        ClaimedAnalysisJob job = NewJob();
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        bool processed = await CreateSut().ProcessNextAsync(CancellationToken.None);

        processed.Should().BeTrue();
        _store.Succeeded.Should().ContainSingle().Which.Should().Be(job.JobId);
        _store.Failed.Should().BeEmpty();
        _store.Released.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessNextAsync_WhenHandlerReturnsFailure_MarksJobFailedWithErrorCode()
    {
        ClaimedAnalysisJob job = NewJob();
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Unexpected("provider unavailable", "ai.provider_unavailable")));

        bool processed = await CreateSut().ProcessNextAsync(CancellationToken.None);

        processed.Should().BeTrue();
        _store.Succeeded.Should().BeEmpty();
        (Guid jobId, string error) = _store.Failed.Should().ContainSingle().Subject;
        jobId.Should().Be(job.JobId);
        error.Should().Contain("ai.provider_unavailable");
    }

    [Fact]
    public async Task ProcessNextAsync_WhenHandlerThrows_MarksJobFailed()
    {
        ClaimedAnalysisJob job = NewJob();
        _store.Enqueue(job);
        _handler
            .Setup(h => h.HandleAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("handler bug"));

        // The worker survives a throwing handler: the job is recorded as failed and
        // the iteration still reports work done.
        bool processed = await CreateSut().ProcessNextAsync(CancellationToken.None);

        processed.Should().BeTrue();
        (Guid jobId, string error) = _store.Failed.Should().ContainSingle().Subject;
        jobId.Should().Be(job.JobId);
        error.Should().Contain("handler bug");
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
                return Result.Success();
            });

        Func<Task> act = () => CreateSut().ProcessNextAsync(cts.Token);

        // Graceful shutdown loses no work: the claim is handed back, not completed.
        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Released.Should().ContainSingle().Which.Should().Be(job.JobId);
        _store.Succeeded.Should().BeEmpty();
        _store.Failed.Should().BeEmpty();
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
            .ReturnsAsync(Result.Success());

        AnalysisJobWorker sut = CreateSut();
        (await sut.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();
        (await sut.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();
        (await sut.ProcessNextAsync(CancellationToken.None)).Should().BeFalse();

        _handler.Verify(h => h.HandleAsync(first, It.IsAny<CancellationToken>()), Times.Once);
        _handler.Verify(h => h.HandleAsync(second, It.IsAny<CancellationToken>()), Times.Once);
        _store.Succeeded.Should().BeEquivalentTo([first.JobId, second.JobId]);
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
}
