using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Worker;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.BackgroundJobs;

/// <summary>
/// Claim-path tests assert oldest-first ordering over a shared database
/// (one Postgres per <see cref="IntegrationCollection"/>), so each test first
/// drains whatever queued rows earlier tests left behind. The collection runs
/// sequentially, which makes the drain deterministic.
/// </summary>
internal static class JobQueueTestHelpers
{
    internal static async Task DrainQueueAsync(this FilerApiFactory factory)
    {
        while (true)
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAnalysisJobStore>();

            ClaimedAnalysisJob? leftover =
                await store.ClaimNextAsync("TestDrain", TestContext.Current.CancellationToken);
            if (leftover is null)
            {
                return;
            }

            await store.MarkSucceededAsync(leftover.JobId, result: null, TestContext.Current.CancellationToken);
        }
    }
}
