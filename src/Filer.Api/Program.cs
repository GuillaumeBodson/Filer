using System.Text.Json;
using Filer.Api.Infrastructure;
using Filer.Modules.Auth;
using Filer.Modules.BackgroundJobs;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.Modules.Storage;
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

// Cross-cutting host services.
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Backstop for unhandled exceptions: logs server-side, returns problem-details
// without leaking internals (Infrastructure/GlobalExceptionHandler.cs, 05-security.md).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Modules register themselves through their public entry point only
// (10-solution-structure.md). The host adds no business logic.
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddBackgroundJobsModule(builder.Configuration);
builder.Services.AddStorageModule(builder.Configuration);

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

app.UseAuthentication();
app.UseAuthorization();

// Each module assembles its routes from its own slices.
app.MapAuthEndpoints();

// Test-only: a throwaway owned resource that exercises the ownership primitive end
// to end (cross-owner → 404) until the first real owned-resource module lands. Gated
// to the Testing environment so it never ships (Infrastructure/OwnershipProbeEndpoints.cs).
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapOwnershipProbeEndpoints();
}

app.Run();

// The top-level statements above compile into an internal Program class. Expose it
// as a public partial so Filer.IntegrationTests can bootstrap the real host through
// WebApplicationFactory<Program> (12-testing-strategy.md). No behaviour is added.
public partial class Program;
