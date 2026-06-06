using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Domain;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.Modules.BackgroundJobs.Worker;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.BackgroundJobs;

/// <summary>
/// The issue's core guarantee, asserted against real Postgres row locking: a job is
/// claimed exactly once no matter how many workers race for it
/// (06-ai-analysis-pipeline.md, "no two workers run the same job";
/// 12-testing-strategy.md, "cover the claim path"). Each concurrent claim runs in
/// its own DI scope — its own DbContext and connection — like real worker instances.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AnalysisJobClaimTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task ClaimNextAsync_StampsRunningStateAtomically()
    {
        await _factory.DrainQueueAsync();
        Guid jobId = await EnqueueAsync(Guid.NewGuid());

        ClaimedAnalysisJob? claimed = await ClaimInOwnScopeAsync();

        claimed.Should().NotBeNull();
        claimed.JobId.Should().Be(jobId);
        claimed.AttemptCount.Should().Be(1, "claiming increments the attempt counter (06)");

        AnalysisJob row = await GetJobAsync(jobId);
        row.Status.Should().Be(AnalysisJobStatus.Running);
        row.StartedAt.Should().NotBeNull();
        row.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ClaimNextAsync_UnderConcurrency_ClaimsEachJobAtMostOnce()
    {
        // One contested job, many racing workers.
        await _factory.DrainQueueAsync();
        Guid jobId = await EnqueueAsync(Guid.NewGuid());

        const int workers = 12;
        ClaimedAnalysisJob?[] outcomes = await Task.WhenAll(
            Enumerable.Range(0, workers).Select(_ => ClaimInOwnScopeAsync()));

        // The contested job went to exactly one claimer; FOR UPDATE SKIP LOCKED
        // makes the losers move on (or come up empty), never double-run.
        outcomes
            .Where(claim => claim is not null && claim.JobId == jobId)
            .Should().HaveCount(1, "no two workers may claim the same job");

        // And globally: no job id of any kind was handed out twice.
        outcomes
            .Where(claim => claim is not null)
            .Select(claim => claim!.JobId)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ClaimNextAsync_WithManyJobsAndAsManyWorkers_HandsOutDistinctJobs()
    {
        await _factory.DrainQueueAsync();

        const int jobs = 5;
        var jobIds = new List<Guid>();
        for (int i = 0; i < jobs; i++)
        {
            jobIds.Add(await EnqueueAsync(Guid.NewGuid()));
        }

        ClaimedAnalysisJob?[] outcomes = await Task.WhenAll(
            Enumerable.Range(0, jobs).Select(_ => ClaimInOwnScopeAsync()));

        List<Guid> claimedIds = outcomes
            .Where(claim => claim is not null)
            .Select(claim => claim!.JobId)
            .ToList();

        claimedIds.Should().OnlyHaveUniqueItems();
        // Every one of our jobs was claimed at most once across the whole race.
        foreach (Guid jobId in jobIds)
        {
            claimedIds.Count(id => id == jobId).Should().BeLessThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task ReleaseAsync_ReturnsAClaimedJobToTheQueueWithoutLosingIt()
    {
        await _factory.DrainQueueAsync();

        Guid documentId = Guid.NewGuid();
        Guid jobId = await EnqueueAsync(documentId);

        ClaimedAnalysisJob? first = await ClaimInOwnScopeAsync();
        first!.JobId.Should().Be(jobId);

        // Graceful shutdown path: the worker hands the job back…
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
            await store.ReleaseAsync(jobId, TestContext.Current.CancellationToken);
        }

        AnalysisJob row = await GetJobAsync(jobId);
        row.Status.Should().Be(AnalysisJobStatus.Queued, "a released job is durable work, not lost work");
        row.StartedAt.Should().BeNull();

        // …and a later claim picks it up again, attempt count advancing.
        ClaimedAnalysisJob? second = await ClaimInOwnScopeAsync();
        second!.JobId.Should().Be(jobId);
        second.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task MarkSucceededAsync_And_MarkFailedAsync_CompleteOnlyRunningJobs()
    {
        await _factory.DrainQueueAsync();

        Guid succeededId = await EnqueueAsync(Guid.NewGuid());
        (await ClaimInOwnScopeAsync())!.JobId.Should().Be(succeededId);

        Guid failedId = await EnqueueAsync(Guid.NewGuid());
        (await ClaimInOwnScopeAsync())!.JobId.Should().Be(failedId);

        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
            await store.MarkSucceededAsync(succeededId, TestContext.Current.CancellationToken);
            await store.MarkFailedAsync(failedId, "ai.provider_unavailable: timeout", TestContext.Current.CancellationToken);
        }

        AnalysisJob succeeded = await GetJobAsync(succeededId);
        succeeded.Status.Should().Be(AnalysisJobStatus.Succeeded);
        succeeded.CompletedAt.Should().NotBeNull();
        succeeded.Error.Should().BeNull();

        AnalysisJob failed = await GetJobAsync(failedId);
        failed.Status.Should().Be(AnalysisJobStatus.Failed);
        failed.CompletedAt.Should().NotBeNull();
        failed.Error.Should().Be("ai.provider_unavailable: timeout");
    }

    private async Task<Guid> EnqueueAsync(Guid documentId)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
        return (await queue.EnqueueAnalysisAsync(documentId, TestContext.Current.CancellationToken)).Value;
    }

    private async Task<ClaimedAnalysisJob?> ClaimInOwnScopeAsync()
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
        return await store.ClaimNextAsync(TestContext.Current.CancellationToken);
    }

    private async Task<AnalysisJob> GetJobAsync(Guid jobId)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        return await db.AnalysisJobs.AsNoTracking()
            .SingleAsync(j => j.Id == jobId, TestContext.Current.CancellationToken);
    }
}
