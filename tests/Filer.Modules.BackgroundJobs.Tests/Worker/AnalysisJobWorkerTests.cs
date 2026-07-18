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
/// The hosted polling loop around <see cref="AnalysisJobProcessor"/> (whose
/// claim/dispatch behaviour is covered by <see cref="AnalysisJobProcessorTests"/>):
/// it respects the Enabled switch, polls while running, stops when asked, and
/// samples the queue depth on its interval (06, 12-testing-strategy.md).
/// </summary>
public sealed class AnalysisJobWorkerTests
{
    private static readonly DateTimeOffset TestTime = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAnalysisJobStore _store = new();
    private readonly Mock<IAnalysisJobHandler> _handler = new();
    private readonly MutableClock _clock = new() { UtcNow = TestTime };

    public AnalysisJobWorkerTests()
    {
        _handler.SetupGet(h => h.ProviderName).Returns("TestProvider");
        _handler
            .Setup(h => h.HandleAsync(It.IsAny<ClaimedAnalysisJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<string?>(null));
    }

    private AnalysisJobWorker CreateSut(BackgroundJobsOptions? options = null)
    {
        // Real DI scopes so the scope-per-iteration behaviour is exercised.
        var services = new ServiceCollection();
        services.AddScoped<IAnalysisJobStore>(_ => _store);
        services.AddScoped<IAnalysisJobHandler>(_ => _handler.Object);
        services.AddMetrics();
        ServiceProvider provider = services.BuildServiceProvider();

        IOptions<BackgroundJobsOptions> workerOptions = Options.Create(options ?? new BackgroundJobsOptions());
        var metrics = new BackgroundJobsMetrics(provider.GetRequiredService<IMeterFactory>());
        var processor = new AnalysisJobProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            workerOptions,
            metrics,
            NullLogger<AnalysisJobProcessor>.Instance);

        return new AnalysisJobWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            workerOptions,
            _clock,
            metrics,
            processor,
            NullLogger<AnalysisJobWorker>.Instance);
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
        AnalysisJobWorker sut = CreateSut(new BackgroundJobsOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
        });

        await sut.StartAsync(CancellationToken.None);
        // Deterministic signal that the loop is live — no wall-clock sleep (12).
        await _store.FirstClaim.WaitAsync(TestContext.Current.CancellationToken);
        await sut.StopAsync(CancellationToken.None);

        // The worker polled while running and stopped when asked — no hang, no throw.
        _store.ClaimCount.Should().BeGreaterThan(0);
    }

    /// <summary>Settable <see cref="IClock"/> so report timing is deterministic (12-testing-strategy.md).</summary>
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
