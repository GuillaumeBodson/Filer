using Filer.Modules.BackgroundJobs.Worker;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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
            .WithTracing(tracing => tracing
                // One span per request (#159); health probes are polled constantly
                // and their spans are pure noise, so they are filtered out.
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                // Outbound HTTP as timed child spans — an Ollama call that times
                // out is a visibly red span, no psql needed (#159).
                .AddHttpClientInstrumentation()
                // The worker's job-processing spans, linked to the originating
                // upload trace (ADR-013).
                .AddSource(BackgroundJobsDiagnostics.ActivitySourceName))
            // Metrics (#60, 04-non-functional.md): the module's pipeline meter
            // (queue depth, job duration, outcomes — emitted since #53, exported
            // from here) plus the built-in ASP.NET Core/Kestrel/HttpClient meters,
            // which cover request latency/error rates and outbound-call timing
            // with no code in the modules.
            .WithMetrics(metrics => metrics
                .AddMeter(BackgroundJobsMetrics.MeterName)
                .AddMeter("Microsoft.AspNetCore.Hosting")
                .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                .AddMeter("System.Net.Http"))
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
