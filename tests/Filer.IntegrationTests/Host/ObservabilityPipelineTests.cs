using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Filer.IntegrationTests.Host;

/// <summary>
/// The OTel pipeline is registered in the real host (ADR-013): the tracer and
/// meter providers exist even with no OTLP endpoint configured (export is
/// opt-in), so #60's metrics and #159's spans flow the moment an endpoint is
/// set. The measurements themselves are asserted at the unit level
/// (BackgroundJobsMetrics via MeterListener, spans via ActivityListener).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ObservabilityPipelineTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public void Host_RegistersTheOpenTelemetryTracerAndMeterProviders()
    {
        _factory.Services.GetService<TracerProvider>().Should().NotBeNull(
            "AddFilerObservability wires tracing unconditionally (ADR-013)");
        _factory.Services.GetService<MeterProvider>().Should().NotBeNull(
            "AddFilerObservability wires metrics unconditionally (#60)");
    }

    [Fact]
    public void ExportedLogRecords_CarryTheRenderedMessageBody()
    {
        // #294: the sink's log stream is read as text — a body left as the raw
        // template ("Document {DocumentId} uploaded…") forces cross-referencing
        // every placeholder against the attribute panel mid-incident.
        OpenTelemetryLoggerOptions options = _factory.Services
            .GetRequiredService<IOptionsMonitor<OpenTelemetryLoggerOptions>>().CurrentValue;

        options.IncludeFormattedMessage.Should().BeTrue(
            "log bodies reaching the sink must be rendered messages, not templates (#294)");
    }

    [Fact]
    public void EfCoreCommandLogging_IsQuietOutsideDevelopment()
    {
        // #295: the polling worker executes a claim query every second; at
        // Information that is ~86k 'Executed DbCommand' events/day of steady-state
        // noise in the sink. The test host runs the base (non-Development)
        // configuration, exactly like production.
        ILogger logger = _factory.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");

        logger.IsEnabled(LogLevel.Information).Should().BeFalse(
            "per-command EF logs flood the sink from the queue poll (#295)");
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue(
            "failed commands must still surface");
    }
}
