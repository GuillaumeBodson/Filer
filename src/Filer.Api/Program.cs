using System.Text.Json;
using Filer.Api.Infrastructure;
using Filer.Modules.AiAnalysis;
using Filer.Modules.Auth;
using Filer.Modules.BackgroundJobs;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.Modules.Documents;
using Filer.Modules.Documents.Persistence;
using Filer.Modules.Folders;
using Filer.Modules.Folders.Persistence;
using Filer.Modules.Search;
using Filer.Modules.Storage;
using Filer.Modules.Tags;
using Filer.Modules.Tags.Persistence;
using Filer.Modules.Auth.Persistence;
using Filer.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Structured logging in JSON across API and workers (04-non-functional.md). Replace
// the default text console with the JSON formatter and include scopes, so the
// framework's request trace context (configured just below) surfaces as queryable
// properties. Levels stay configuration-driven via appsettings (Logging:LogLevel).
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});

// Surface the W3C trace context on every log line. Combined with IncludeScopes
// above, each entry carries the request's TraceId/SpanId as structured properties,
// tying a request to the work it spawns — and propagating to the future worker tier
// for free via traceparent. Set explicitly rather than leaning on the host default.
builder.Logging.Configure(options =>
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId
        | ActivityTrackingOptions.SpanId
        | ActivityTrackingOptions.ParentId);

if (builder.Environment.IsDevelopment())
{
    // Mirror logs to the IDE/debug window during development only.
    builder.Logging.AddDebug();
}

// Cross-cutting host services. The document transformer keeps nullable complex
// properties out of oneOf-union territory Kiota cannot deserialize
// (Infrastructure/NullableRefSchemaTransformer.cs, ADR-011).
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<NullableRefSchemaTransformer>());
builder.Services.AddProblemDetails();

// Strict JSON numbers. The web default (AllowReadingFromString) makes the OpenAPI
// generator describe every number as ["integer","string"], a union Kiota can only
// map to UntypedNode - which un-types PagedResult and sizes in the generated client
// (ADR-011: client quality depends on contract quality). Numbers are numbers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict);

// CORS for browser clients on another origin (#148, 05-security.md): origins come
// from configuration only — no wildcard, off when the list is empty (same-origin
// deployments behind a reverse proxy need no CORS at all). The bearer scheme needs
// the Authorization header; no cookies, so credentials stay disallowed.
string[] corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

// Backstop for unhandled exceptions: logs server-side, returns problem-details
// without leaking internals (Infrastructure/GlobalExceptionHandler.cs, 05-security.md).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Modules register themselves through their public entry point only
// (10-solution-structure.md). The host adds no business logic.
builder.Services.AddAiAnalysisModule(builder.Configuration);
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddBackgroundJobsModule(builder.Configuration);
builder.Services.AddStorageModule(builder.Configuration);
builder.Services.AddDocumentsModule(builder.Configuration);
builder.Services.AddFoldersModule(builder.Configuration);
builder.Services.AddSearchModule(builder.Configuration);
builder.Services.AddTagsModule(builder.Configuration);

builder.Services.AddAuthorization();

// Resolve the authenticated caller from the validated token's claims so feature
// services and the OwnershipGuard never trust a client-supplied id (05-security.md).
// The adapter reads the current ClaimsPrincipal via the HTTP context accessor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

var app = builder.Build();

// Apply pending migrations for each module's DbContext at startup so the
// walking skeleton proves host + auth + persistence wire together.
// Add the initial migration first (see README): the Auth module owns its
// migrations under Filer.Modules.Auth/Persistence/Migrations.
await using (var scope = app.Services.CreateAsyncScope())
{
    var authDb = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await authDb.Database.MigrateAsync();

    var jobsDb = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
    await jobsDb.Database.MigrateAsync();

    var documentsDb = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
    await documentsDb.Database.MigrateAsync();

    var foldersDb = scope.ServiceProvider.GetRequiredService<FoldersDbContext>();
    await foldersDb.Database.MigrateAsync();

    var tagsDb = scope.ServiceProvider.GetRequiredService<TagsDbContext>();
    await tagsDb.Database.MigrateAsync();
}

// Problem-details for unhandled exceptions and non-success status codes (03-api-specification.md).
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Skip the HTTPS redirect in Development: local containers expose plain HTTP, and
// tooling that injects an https port (e.g. VS container tools setting
// ASPNETCORE_HTTPS_PORTS) would turn every http call into a 307 - clients drop the
// Authorization header when following it, breaking authenticated endpoints.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

if (corsOrigins.Length > 0)
{
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();

// Each module assembles its routes from its own slices.
app.MapAuthEndpoints();
app.MapDocumentsEndpoints();
app.MapFoldersEndpoints();
app.MapSearchEndpoints();
app.MapTagsEndpoints();

app.Run();

// The top-level statements above compile into an internal Program class. Expose it
// as a public partial so Filer.IntegrationTests can bootstrap the real host through
// WebApplicationFactory<Program> (12-testing-strategy.md). No behaviour is added.
public partial class Program;
