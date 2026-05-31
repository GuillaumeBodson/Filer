using Filer.SharedKernel.Time;

namespace Filer.Modules.Auth.Tests.TestSupport;

/// <summary>
/// Deterministic <see cref="IClock"/> for tests. Time is an injected seam
/// (12-testing-strategy.md) so no test depends on the wall clock.
/// </summary>
internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}
