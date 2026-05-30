namespace Filer.SharedKernel.Time;

/// <summary>
/// Abstraction over the current UTC time so time-dependent logic stays testable.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
