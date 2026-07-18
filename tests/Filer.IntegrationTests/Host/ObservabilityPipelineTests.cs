using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
}
