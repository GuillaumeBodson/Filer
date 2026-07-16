using Filer.Modules.BackgroundJobs.Contracts;
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

    public List<(Guid JobId, string? Result)> Succeeded { get; } = [];

    public List<(Guid JobId, string Error)> Failed { get; } = [];

    public List<Guid> Cancelled { get; } = [];

    public List<(Guid JobId, string Error, TimeSpan Delay)> Retried { get; } = [];

    public List<Guid> Released { get; } = [];

    public int ClaimCount { get; private set; }

    public int CountQueuedCalls { get; private set; }

    /// <summary>Completes on the first claim — a deterministic "the loop is running" signal, no sleeps (12).</summary>
    public Task FirstClaim => _firstClaim.Task;

    private readonly TaskCompletionSource _firstClaim = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Enqueue(ClaimedAnalysisJob job) => _pending.Enqueue(job);

    public Task<ClaimedAnalysisJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClaimCount++;
        _firstClaim.TrySetResult();
        return Task.FromResult(_pending.Count > 0 ? _pending.Dequeue() : null);
    }

    public Task MarkSucceededAsync(Guid jobId, string? result, CancellationToken cancellationToken)
    {
        Succeeded.Add((jobId, result));
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid jobId, string error, CancellationToken cancellationToken)
    {
        Failed.Add((jobId, error));
        return Task.CompletedTask;
    }

    public Task MarkCancelledAsync(Guid jobId, CancellationToken cancellationToken)
    {
        Cancelled.Add(jobId);
        return Task.CompletedTask;
    }

    public Task ScheduleRetryAsync(Guid jobId, string error, TimeSpan delay, CancellationToken cancellationToken)
    {
        Retried.Add((jobId, error, delay));
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(Guid jobId, CancellationToken cancellationToken)
    {
        Released.Add(jobId);
        return Task.CompletedTask;
    }

    public Task<int> CountQueuedAsync(CancellationToken cancellationToken)
    {
        CountQueuedCalls++;
        return Task.FromResult(_pending.Count);
    }
}
