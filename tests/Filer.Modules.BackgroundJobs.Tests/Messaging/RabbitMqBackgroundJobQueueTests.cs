using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Messaging;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using Xunit;

namespace Filer.Modules.BackgroundJobs.Tests.Messaging;

/// <summary>
/// The outbox-relay decorator (ADR-008): the row commit stays authoritative — a
/// wake-up is published only after a successful enqueue, and a publish failure
/// never fails the accepted job (broker-down degrades to sweeper delivery).
/// </summary>
public sealed class RabbitMqBackgroundJobQueueTests
{
    private readonly Mock<IBackgroundJobQueue> _inner = new();
    private readonly Mock<IAnalysisJobDispatcher> _dispatcher = new();
    private readonly FakeLogger<RabbitMqBackgroundJobQueue> _logger = new();

    private RabbitMqBackgroundJobQueue CreateSut() => new(_inner.Object, _dispatcher.Object, _logger);

    [Fact]
    public async Task EnqueueAnalysisAsync_AfterASuccessfulEnqueue_PublishesTheWakeUpForThatJob()
    {
        Guid documentId = Guid.NewGuid();
        Guid jobId = Guid.NewGuid();
        _inner
            .Setup(q => q.EnqueueAnalysisAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(jobId));

        Result<Guid> result = await CreateSut().EnqueueAnalysisAsync(documentId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(jobId);
        _dispatcher.Verify(d => d.PublishJobReadyAsync(jobId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueAnalysisAsync_WhenTheEnqueueFails_PublishesNothing()
    {
        _inner
            .Setup(q => q.EnqueueAnalysisAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Guid>(Error.Validation("bad", "jobs.document_id_required")));

        Result<Guid> result = await CreateSut().EnqueueAnalysisAsync(Guid.Empty, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _dispatcher.Verify(
            d => d.PublishJobReadyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnqueueAnalysisAsync_WhenTheBrokerIsDown_StillReturnsTheAcceptedJob()
    {
        Guid jobId = Guid.NewGuid();
        _inner
            .Setup(q => q.EnqueueAnalysisAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(jobId));
        _dispatcher
            .Setup(d => d.PublishJobReadyAsync(jobId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unreachable"));

        Result<Guid> result = await CreateSut().EnqueueAnalysisAsync(Guid.NewGuid(), CancellationToken.None);

        // The row is committed - the job must not be failed retroactively; the
        // sweeper recovers it (ADR-008, broker-down degradation).
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(jobId);

        FakeLogRecord warning = _logger.Collector.GetSnapshot()
            .Should().ContainSingle(r => r.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain(jobId.ToString());
        warning.Message.Should().Contain("sweeper");
    }

    [Fact]
    public async Task CancelForDocumentAsync_DelegatesWithoutTouchingTheBroker()
    {
        Guid documentId = Guid.NewGuid();
        _inner
            .Setup(q => q.CancelForDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(2));

        Result<int> result = await CreateSut().CancelForDocumentAsync(documentId, CancellationToken.None);

        result.Value.Should().Be(2);
        _dispatcher.VerifyNoOtherCalls();
    }
}
