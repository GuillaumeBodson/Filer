using Filer.Api.Infrastructure;
using Filer.Modules.Auth;
using Filer.Modules.Auth.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting host services.
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Backstop for unhandled exceptions: logs server-side, returns problem-details
// without leaking internals (Infrastructure/GlobalExceptionHandler.cs, 05-security.md).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Modules register themselves through their public entry point only
// (10-solution-structure.md). The host adds no business logic.
builder.Services.AddAuthModule(builder.Configuration);

builder.Services.AddAuthorization();

var app = builder.Build();

// Apply pending migrations for each module's DbContext at startup so the
// walking skeleton proves host + auth + persistence wire together.
// Add the initial migration first (see README): the Auth module owns its
// migrations under Filer.Modules.Auth/Persistence/Migrations.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await db.Database.MigrateAsync();
}

// Problem-details for unhandled exceptions and non-success status codes (03-api-specification.md).
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Each module assembles its routes from its own slices.
app.MapAuthEndpoints();

app.Run();
