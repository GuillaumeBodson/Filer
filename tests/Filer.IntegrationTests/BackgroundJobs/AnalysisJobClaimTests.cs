using System.Text.Json.Nodes;
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

        const string resultJson = """{"suggestedFolder":null,"suggestedTags":[{"name":"invoice","confidence":0.5}],"duplicateSignals":[]}""";
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
            await store.MarkSucceededAsync(succeededId, resultJson, TestContext.Current.CancellationToken);
            await store.MarkFailedAsync(failedId, "ai.provider_unavailable: timeout", TestContext.Current.CancellationToken);
        }

        AnalysisJob succeeded = await GetJobAsync(succeededId);
        succeeded.Status.Should().Be(AnalysisJobStatus.Succeeded);
        succeeded.CompletedAt.Should().NotBeNull();
        succeeded.Error.Should().BeNull();

        // The serialized result landed in the JSONB column. Postgres normalises
        // jsonb formatting, so compare parsed shapes, not raw strings.
        succeeded.Result.Should().NotBeNull();
        JsonNode.DeepEquals(JsonNode.Parse(succeeded.Result!), JsonNode.Parse(resultJson))
            .Should().BeTrue("MarkSucceededAsync persists the handler's result as AnalysisJob.Result (06)");

        AnalysisJob failed = await GetJobAsync(failedId);
        failed.Status.Should().Be(AnalysisJobStatus.Failed);
        failed.CompletedAt.Should().NotBeNull();
        failed.Error.Should().Be("ai.provider_unavailable: timeout");
    }

    [Fact]
    public async Task ScheduleRetryAsync_RequeuesWithBackoff_AndTheClaimSkipsItUntilDue()
    {
        await _factory.DrainQueueAsync();

        Guid documentId = Guid.NewGuid();
        Guid jobId = await EnqueueAsync(documentId);
        (await ClaimInOwnScopeAsync())!.JobId.Should().Be(jobId);

        // The worker requeues the failed attempt with a far-future backoff…
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
            await store.ScheduleRetryAsync(
                jobId, "ai.provider_unavailable: timeout", TimeSpan.FromHours(1),
                TestContext.Current.CancellationToken);
        }

        AnalysisJob row = await GetJobAsync(jobId);
        row.Status.Should().Be(AnalysisJobStatus.Queued, "a retryable failure goes back to Queued (06)");
        row.Error.Should().Be("ai.provider_unavailable: timeout");
        row.StartedAt.Should().BeNull();
        row.NextAttemptAt.Should().NotBeNull();
        row.CompletedAt.Should().BeNull();

        // …and the claim respects the backoff: the job is not due, so nothing is claimed.
        ClaimedAnalysisJob? premature = await ClaimInOwnScopeAsync();
        premature.Should().BeNull("a job backing off must not be reclaimed before NextAttemptAt");

        // Leave no leftovers for the shared-database suite.
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobQueue>();
            (await queue.CancelForDocumentAsync(documentId, TestContext.Current.CancellationToken))
                .Value.Should().Be(1);
        }
    }

    [Fact]
    public async Task ScheduleRetryAsync_WithAnElapsedBackoff_MakesTheJobClaimableAgain()
    {
        await _factory.DrainQueueAsync();

        Guid jobId = await EnqueueAsync(Guid.NewGuid());
        (await ClaimInOwnScopeAsync())!.JobId.Should().Be(jobId);

        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
            await store.ScheduleRetryAsync(
                jobId, "transient", TimeSpan.Zero, TestContext.Current.CancellationToken);
        }

        // The backoff has elapsed: a fresh claim picks the retry up, attempt advancing,
        // and clears the schedule stamp.
        ClaimedAnalysisJob? retry = await ClaimInOwnScopeAsync();
        retry.Should().NotBeNull();
        retry.JobId.Should().Be(jobId);
        retry.AttemptCount.Should().Be(2, "the retry is a new attempt (06)");

        AnalysisJob row = await GetJobAsync(jobId);
        row.Status.Should().Be(AnalysisJobStatus.Running);
        row.NextAttemptAt.Should().BeNull("claiming clears the backoff stamp");

        await using (AsyncServiceScope cleanup = _factory.Services.CreateAsyncScope())
        {
            var store = cleanup.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
            await store.MarkSucceededAsync(jobId, result: null, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task MarkCancelledAsync_CancelsARunningJob()
    {
        await _factory.DrainQueueAsync();

        Guid jobId = await EnqueueAsync(Guid.NewGuid());
        (await ClaimInOwnScopeAsync())!.JobId.Should().Be(jobId);

        // The handler found the document gone; the worker cancels the job (06).
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();
            await store.MarkCancelledAsync(jobId, TestContext.Current.CancellationToken);
        }

        AnalysisJob row = await GetJobAsync(jobId);
        row.Status.Should().Be(AnalysisJobStatus.Cancelled);
        row.CompletedAt.Should().NotBeNull();
        row.Error.Should().BeNull();
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
