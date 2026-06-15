using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.BackgroundJobs.Domain;
using Filer.Modules.BackgroundJobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// Arrange helpers for the analysis endpoints (#54/#55): uploading queues a real
/// job (the upload slice enqueues through IBackgroundJobQueue), but the hosted
/// worker is disabled in the test host (<see cref="FilerApiFactory"/>), so tests
/// that need a terminal job drive the latest row to its outcome directly through
/// the module's DbContext — same stance as the soft-delete arrange steps this
/// project already takes (see Filer.IntegrationTests.csproj).
/// </summary>
internal static class AnalysisJobTestHelpers
{
    /// <summary>Marks the document's latest analysis job Succeeded with the given result JSON.</summary>
    internal static Task<Guid> CompleteLatestAnalysisJobAsync(
        this FilerApiFactory factory, Guid documentId, string resultJson) =>
        factory.MutateLatestAnalysisJobAsync(documentId, job =>
        {
            job.Status = AnalysisJobStatus.Succeeded;
            job.Result = resultJson;
            job.CompletedAt = DateTimeOffset.UtcNow;
        });

    /// <summary>Marks the document's latest analysis job terminally Failed with the given error.</summary>
    internal static Task<Guid> FailLatestAnalysisJobAsync(
        this FilerApiFactory factory, Guid documentId, string error) =>
        factory.MutateLatestAnalysisJobAsync(documentId, job =>
        {
            job.Status = AnalysisJobStatus.Failed;
            job.Error = error;
            job.CompletedAt = DateTimeOffset.UtcNow;
        });

    private static async Task<Guid> MutateLatestAnalysisJobAsync(
        this FilerApiFactory factory, Guid documentId, Action<AnalysisJob> mutate)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();

        AnalysisJob job = await db.AnalysisJobs
            .Where(j => j.DocumentId == documentId)
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .FirstAsync(TestContext.Current.CancellationToken);

        mutate(job);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return job.Id;
    }
}
