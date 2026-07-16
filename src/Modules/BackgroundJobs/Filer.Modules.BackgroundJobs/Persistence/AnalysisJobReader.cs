using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.BackgroundJobs.Persistence;

/// <summary>
/// EF Core implementation of the cross-module read surface
/// (<see cref="IAnalysisJobReader"/>) over the module's context. Latest-first by
/// <c>CreatedAt</c> with the id as tiebreaker so re-queued documents resolve
/// deterministically. Projects to the Contracts snapshot — the Domain entity and
/// its <c>Error</c> column never cross the boundary (05-security.md).
/// </summary>
internal sealed class AnalysisJobReader(JobsDbContext dbContext) : IAnalysisJobReader
{
    public async Task<AnalysisJobSnapshot?> FindLatestForDocumentAsync(
        Guid documentId, CancellationToken cancellationToken)
    {
        AnalysisJob? job = await dbContext.AnalysisJobs
            .AsNoTracking()
            .Where(j => j.DocumentId == documentId)
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return job is null
            ? null
            : new AnalysisJobSnapshot(job.Id, Map(job.Status), job.Result, job.CompletedAt);
    }

    /// <summary>
    /// Explicit Domain → Contracts mapping: the persisted enum must not leak, and
    /// an unmapped new state must fail loudly here rather than surface wrongly.
    /// </summary>
    private static AnalysisJobState Map(AnalysisJobStatus status) => status switch
    {
        AnalysisJobStatus.Queued => AnalysisJobState.Queued,
        AnalysisJobStatus.Running => AnalysisJobState.Running,
        AnalysisJobStatus.Succeeded => AnalysisJobState.Succeeded,
        AnalysisJobStatus.Failed => AnalysisJobState.Failed,
        AnalysisJobStatus.Cancelled => AnalysisJobState.Cancelled,
        _ => throw new InvalidOperationException($"Unmapped analysis job status '{status}'."),
    };
}
