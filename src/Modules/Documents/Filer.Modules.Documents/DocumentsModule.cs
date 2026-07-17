using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Features.AddTag;
using Filer.Modules.Documents.Features.ApplyDocumentAnalysis;
using Filer.Modules.Documents.Features.Delete;
using Filer.Modules.Documents.Features.DownloadContent;
using Filer.Modules.Documents.Features.GetDocumentAnalysis;
using Filer.Modules.Documents.Features.GetMetadata;
using Filer.Modules.Documents.Features.GetTags;
using Filer.Modules.Documents.Features.ListDocuments;
using Filer.Modules.Documents.Features.RemoveTag;
using Filer.Modules.Documents.Features.ReplaceTags;
using Filer.Modules.Documents.Features.UpdateMetadata;
using Filer.Modules.Documents.Features.Upload;
using Filer.Modules.Documents.Files;
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
            // Sniffing is mandatory for every accepted upload (05-security.md), and
            // FileSignatures fails closed for types it has no signature for — so an
            // allow-list entry without one could never be uploaded. Surface that
            // misconfiguration here, at startup, instead of as request-time 415s.
            .Validate(
                options => options.AllowedContentTypes
                    .Select(UploadDocumentValidator.NormalizeMediaType)
                    .All(FileSignatures.IsKnown),
                $"'{DocumentsOptions.SectionName}:{nameof(DocumentsOptions.AllowedContentTypes)}' contains a media " +
                "type without a registered content signature. Sniffing is mandatory (05-security.md); register a " +
                "signature in FileSignatures/KnownMediaTypes before allowing the type.")
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

        // Public surface for other modules (Folders' delete cascade, ADR-007); the
        // implementation stays internal behind the Contracts interface.
        services.AddScoped<IFolderDocumentRemover, FolderDocumentRemover>();

        // Public surface for the Tags module (the tag-delete cascade, ADR-009/#48);
        // the implementation stays internal behind the Contracts interface.
        services.AddScoped<IDocumentTagRemover, DocumentTagRemover>();

        // Public surface for the AI analysis worker (#53): load a document's
        // analysis context and mark it Ready; internal behind the Contracts interface.
        services.AddScoped<IDocumentAnalysisGateway, DocumentAnalysisGateway>();

        // Public surface for providers that sample a candidate folder's contents
        // mid-analysis (#119); internal behind the Contracts interface.
        services.AddScoped<IFolderContentLookup, FolderContentLookup>();

        // Feature services (vertical slices).
        services.AddScoped<UploadDocumentService>();
        services.AddScoped<DownloadDocumentContentService>();
        services.AddScoped<GetDocumentMetadataService>();
        services.AddScoped<ListDocumentsService>();
        services.AddScoped<UpdateDocumentMetadataService>();
        services.AddScoped<DeleteDocumentService>();
        services.AddScoped<ReplaceDocumentTagsService>();
        services.AddScoped<AddDocumentTagService>();
        services.AddScoped<RemoveDocumentTagService>();
        services.AddScoped<GetDocumentTagsService>();
        services.AddScoped<GetDocumentAnalysisService>();
        services.AddScoped<ApplyDocumentAnalysisService>();

        return services;
    }

    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup(DocumentsRoutes.BasePath);

        group.MapListDocuments();
        group.MapUploadDocument();
        group.MapDownloadDocumentContent();
        group.MapGetDocumentMetadata();
        group.MapUpdateDocumentMetadata();
        group.MapDeleteDocument();
        group.MapReplaceDocumentTags();
        group.MapAddDocumentTag();
        group.MapRemoveDocumentTag();
        group.MapGetDocumentTags();
        group.MapGetDocumentAnalysis();
        group.MapApplyDocumentAnalysis();

        return routes;
    }
}
