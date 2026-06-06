using Filer.SharedKernel.Time;

namespace Filer.Modules.Documents.Tests.TestSupport;

/// <summary>Deterministic clock seam (12-testing-strategy.md).</summary>
internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}
