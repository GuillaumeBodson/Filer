using Filer.Modules.Documents.Features.Upload;
using Filer.Modules.Documents.Persistence;
using Filer.SharedKernel.Configuration;
using Filer.SharedKernel.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Filer.Modules.Documents;

/// <summary>
/// Registration entry point for the Documents module. The host invokes
/// <see cref="AddDocumentsModule"/> and <see cref="MapDocumentsEndpoints"/> only;
/// it never reaches into module internals (10-solution-structure.md).
/// </summary>
public static class DocumentsModule
{
    /// <summary>
    /// Headroom on top of <see cref="DocumentsOptions.MaxUploadBytes"/> for
    /// multipart boundaries and form fields, so a file of exactly the configured
    /// maximum still fits in the request and oversize is reported by the slice as
    /// a problem-details 413 rather than a severed connection.
    /// </summary>
    private const long UploadRequestOverheadBytes = 1024 * 1024;

    public static IServiceCollection AddDocumentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Cross-cutting clock primitive; TryAdd so the host may also register it.
        services.TryAddSingleton<IClock, SystemClock>();

        services.AddOptions<DocumentsOptions>()
            .Bind(configuration.GetSection(DocumentsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Eager read, mirroring the other modules: the request-size limits below
        // are registration-time decisions. Every option has a safe default
        // (04-non-functional.md V1 values), so a missing section is not an error.
        DocumentsOptions options = configuration.GetSection(DocumentsOptions.SectionName).Get<DocumentsOptions>()
            ?? new DocumentsOptions();

        // The module owns its data in the 'documents' Postgres schema.
        string connectionString = configuration.GetConnectionString(ConnectionStringNames.Postgres)
            ?? throw new InvalidOperationException($"The '{ConnectionStringNames.Postgres}' connection string is missing.");

        services.AddDbContext<DocumentsDbContext>(dbOptions =>
            dbOptions.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DocumentsDbContext.Schema)));

        // Lift the server/body limits to the configured ceiling plus overhead so the
        // 413 decision is made by the upload slice with a problem-details body
        // (03/04), not by Kestrel's default 30 MB cut-off mid-stream. Host-wide by
        // necessity (Kestrel limits are global); acceptable for the V1 single-host
        // deployment and revisitable per-endpoint if another module needs less.
        services.Configure<KestrelServerOptions>(kestrel =>
            kestrel.Limits.MaxRequestBodySize = options.MaxUploadBytes + UploadRequestOverheadBytes);
        services.Configure<FormOptions>(form =>
            form.MultipartBodyLengthLimit = options.MaxUploadBytes + UploadRequestOverheadBytes);

        services.AddScoped<IDocumentStore, EfDocumentStore>();

        // Feature services (vertical slices).
        services.AddScoped<UploadDocumentService>();

        return services;
    }

    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup(DocumentsRoutes.BasePath);

        group.MapUploadDocument();

        return routes;
    }
}
