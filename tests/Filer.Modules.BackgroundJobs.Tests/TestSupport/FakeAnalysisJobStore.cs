using Filer.Modules.BackgroundJobs.Worker;

namespace Filer.Modules.BackgroundJobs.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IAnalysisJobStore"/> recording the worker's bookkeeping
/// calls. The store is a designed seam (12-testing-strategy.md), so the worker's
/// orchestration is asserted here without a database.
/// </summary>
internal sealed class FakeAnalysisJobStore : IAnalysisJobStore
{
    private readonly Queue<ClaimedAnalysisJob> _pending = new();

    public List<Guid> Succeeded { get; } = [];

    public List<(Guid JobId, string Error)> Failed { get; } = [];

    public List<Guid> Released { get; } = [];

    public int ClaimCount { get; private set; }

    public void Enqueue(ClaimedAnalysisJob job) => _pending.Enqueue(job);

    public Task<ClaimedAnalysisJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClaimCount++;
        return Task.FromResult(_pending.Count > 0 ? _pending.Dequeue() : null);
    }

    public Task MarkSucceededAsync(Guid jobId, CancellationToken cancellationToken)
    {
        Succeeded.Add(jobId);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid jobId, string error, CancellationToken cancellationToken)
    {
        Failed.Add((jobId, error));
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(Guid jobId, CancellationToken cancellationToken)
    {
        Released.Add(jobId);
        return Task.CompletedTask;
    }
}
