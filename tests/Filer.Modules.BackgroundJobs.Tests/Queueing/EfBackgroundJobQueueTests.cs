using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.Modules.BackgroundJobs.Queueing;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Filer.Modules.BackgroundJobs.Tests.Queueing;

/// <summary>
/// Unit coverage for the queue's validation failures — each <c>Error</c> path
/// (12-testing-strategy.md). The success paths hit Postgres and live in
/// Filer.IntegrationTests (no EF in-memory; don't mock what you own).
/// </summary>
public sealed class EfBackgroundJobQueueTests
{
    private static EfBackgroundJobQueue CreateSut()
    {
        // A real context that is never asked to connect: validation fails first.
        var options = new DbContextOptionsBuilder<JobsDbContext>()
            .UseNpgsql("Host=localhost;Database=never-connected")
            .Options;

        return new EfBackgroundJobQueue(
            new JobsDbContext(options),
            new FixedClock(new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<EfBackgroundJobQueue>.Instance);
    }

    [Fact]
    public async Task EnqueueAnalysisAsync_WhenDocumentIdEmpty_ReturnsValidationError()
    {
        Result<Guid> result = await CreateSut().EnqueueAnalysisAsync(Guid.Empty, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(BackgroundJobsErrorCodes.DocumentIdRequired);
    }

    [Fact]
    public async Task CancelForDocumentAsync_WhenDocumentIdEmpty_ReturnsValidationError()
    {
        Result<int> result = await CreateSut().CancelForDocumentAsync(Guid.Empty, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(BackgroundJobsErrorCodes.DocumentIdRequired);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
