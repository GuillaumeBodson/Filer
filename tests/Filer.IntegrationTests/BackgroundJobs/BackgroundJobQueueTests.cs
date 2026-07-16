using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Domain;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.Modules.BackgroundJobs.Worker;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.BackgroundJobs;

/// <summary>
/// The DB-backed queue against the real Postgres-owned <c>jobs</c> schema:
/// enqueueing commits a Queued row before returning (durable — a crash loses no
/// accepted work), and cancellation flips a document's queued and running jobs to
/// Cancelled — a final state even against a worker finishing concurrently
/// (06-ai-analysis-pipeline.md). The module maps no endpoints, so the contract is
/// exercised directly through the host's DI container.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BackgroundJobQueueTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task EnqueueAnalysisAsync_PersistsAQueuedJobVisibleToANewScope()
    {
        Guid documentId = Guid.NewGuid();

        Guid jobId;
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
            Result<Guid> result = await queue.EnqueueAnalysisAsync(documentId, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            jobId = result.Value;
        }

        // A fresh scope (new DbContext, new connection) sees the committed row:
        // the accepted job is durable, not an artifact of a tracked context.
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
            AnalysisJob job = await db.AnalysisJobs.AsNoTracking()
                .SingleAsync(j => j.Id == jobId, TestContext.Current.CancellationToken);

            job.DocumentId.Should().Be(documentId);
            job.Status.Should().Be(AnalysisJobStatus.Queued);
            job.AttemptCount.Should().Be(0);
            job.StartedAt.Should().BeNull();
            job.CompletedAt.Should().BeNull();
            job.CreatedAt.Should().NotBe(default);
        }
    }

    [Fact]
    public async Task CancelForDocumentAsync_CancelsQueuedAndRunningJobs()
    {
        await _factory.DrainQueueAsync();

        Guid documentId = Guid.NewGuid();

        // Two jobs for the document; claim one so it is Running.
        Guid queuedJobId;
        Guid runningJobId;
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
            runningJobId = (await queue.EnqueueAnalysisAsync(documentId, TestContext.Current.CancellationToken)).Value;
            queuedJobId = (await queue.EnqueueAnalysisAsync(documentId, TestContext.Current.CancellationToken)).Value;

            var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
            ClaimedAnalysisJob? claimed =
                await store.ClaimNextAsync("TestProvider", TestContext.Current.CancellationToken);
            claimed!.JobId.Should().Be(runningJobId, "claiming takes the oldest queued job");
        }

        int cancelled;
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
            Result<int> result =
                await queue.CancelForDocumentAsync(documentId, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            cancelled = result.Value;
        }

        cancelled.Should().Be(2, "queued and running jobs are both cancelled (06)");

        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();

            AnalysisJob queuedJob = await db.AnalysisJobs.AsNoTracking()
                .SingleAsync(j => j.Id == queuedJobId, TestContext.Current.CancellationToken);
            queuedJob.Status.Should().Be(AnalysisJobStatus.Cancelled);
            queuedJob.CompletedAt.Should().NotBeNull();

            AnalysisJob runningJob = await db.AnalysisJobs.AsNoTracking()
                .SingleAsync(j => j.Id == runningJobId, TestContext.Current.CancellationToken);
            runningJob.Status.Should().Be(AnalysisJobStatus.Cancelled,
                "deleting a document cancels its in-flight jobs too (06)");
        }

        // The worker that still holds the formerly running job now finishes: its
        // completion write is guarded on Status == Running, so Cancelled is final.
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
            await store.MarkSucceededAsync(runningJobId, result: null, TestContext.Current.CancellationToken);

            var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
            AnalysisJob runningJob = await db.AnalysisJobs.AsNoTracking()
                .SingleAsync(j => j.Id == runningJobId, TestContext.Current.CancellationToken);
            runningJob.Status.Should().Be(AnalysisJobStatus.Cancelled,
                "a cancelled job can never be resurrected by a late worker");
        }
    }

    [Fact]
    public async Task CancelForDocumentAsync_WhenDocumentHasNoJobs_SucceedsWithZero()
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();

        Result<int> result =
            await queue.CancelForDocumentAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task Host_RegistersTheAnalysisJobWorkerAsAHostedService()
    {
        // The worker is wired by AddBackgroundJobsModule; in this test host it is
        // disabled by configuration, but its registration must hold.
        _factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Should().Contain(service => service is AnalysisJobWorker);
    }
}
