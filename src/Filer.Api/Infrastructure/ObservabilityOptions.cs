using OpenTelemetry.Exporter;

namespace Filer.Api.Infrastructure;

/// <summary>
/// Host observability settings (ADR-013): OpenTelemetry is the emit layer for
/// traces, metrics and logs, and export is opt-in — with no OTLP endpoint
/// configured the SDK stays registered but nothing leaves the process, which
/// keeps tests and default dev runs silent (13-code-quality-and-design.md,
/// configuration over literals).
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// OTel resource <c>service.name</c>. The API host defaults to
    /// <c>filer-api</c>; a future split-out worker host sets <c>filer-worker</c>
    /// so signals stay attributable after the split (ADR-013, 04-non-functional.md).
    /// </summary>
    public string ServiceName { get; set; } = "filer-api";

    public OtlpOptions Otlp { get; set; } = new();

    public sealed class OtlpOptions
    {
        /// <summary>Collector endpoint (absolute URI). Null or empty disables export.</summary>
        public string? Endpoint { get; set; }

        public OtlpExportProtocol Protocol { get; set; } = OtlpExportProtocol.Grpc;
    }
}
