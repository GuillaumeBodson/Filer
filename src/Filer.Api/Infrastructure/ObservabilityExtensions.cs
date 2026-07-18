using Filer.Modules.BackgroundJobs.Worker;
using OpenTelemetry;
using OpenTelemetry.Resources;

namespace Filer.Api.Infrastructure;

/// <summary>
/// OpenTelemetry bootstrap (ADR-013): one shared pipeline for traces, metrics and
/// logs, confined to the host. Modules emit through the BCL primitives only
/// (<c>ActivitySource</c>/<c>Meter</c>/<c>ILogger</c>) and never reference the
/// OTel SDK, so the emit layer stays swappable (04-non-functional.md).
/// </summary>
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddFilerObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ObservabilityOptions>()
            .Bind(builder.Configuration.GetSection(ObservabilityOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ServiceName),
                "Observability:ServiceName must not be empty.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.Otlp.Endpoint)
                    || Uri.TryCreate(options.Otlp.Endpoint, UriKind.Absolute, out _),
                "Observability:Otlp:Endpoint must be an absolute URI when set.")
            .ValidateOnStart();

        // Also read eagerly: whether the OTLP exporter joins the pipeline is a
        // registration-time decision. ValidateOnStart above still guards the values.
        ObservabilityOptions options = builder.Configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        IOpenTelemetryBuilder otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                options.ServiceName,
                serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString(),
                serviceInstanceId: Environment.MachineName))
            // The worker's job-processing spans, linked to the originating upload
            // trace (ADR-013). ASP.NET Core/HttpClient instrumentation lands with #159.
            .WithTracing(tracing => tracing.AddSource(BackgroundJobsDiagnostics.ActivitySourceName))
            // Route ILogger records through OTel alongside the JSON console: same
            // message templates, now exportable with trace correlation (ADR-005).
            .WithLogging();

        if (!string.IsNullOrWhiteSpace(options.Otlp.Endpoint))
        {
            otel.UseOtlpExporter(options.Otlp.Protocol, new Uri(options.Otlp.Endpoint));
        }

        return builder;
    }
}
