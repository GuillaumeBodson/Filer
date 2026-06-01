using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Filer.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API host in-process against a real PostgreSQL 17 — the
/// integration layer mandated by 12-testing-strategy.md (no EF in-memory/SQLite).
///
/// Postgres resolution mirrors both the strategy doc and the CI wiring:
/// <list type="bullet">
///   <item>locally / dev: own an ephemeral container via Testcontainers, mirroring
///   <c>docker-compose.yml</c> (postgres:17);</item>
///   <item>CI: when <c>ConnectionStrings__Postgres</c> is supplied (the Postgres
///   service in <c>.github/workflows/ci.yml</c>), reuse it instead of nesting a
///   container in the runner.</item>
/// </list>
/// The host applies its EF migrations at startup (<c>Program.cs</c>), so the schema
/// is created against whichever database is resolved here.
/// </summary>
public sealed class FilerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ExternalConnectionEnvVar = "ConnectionStrings__Postgres";

    // Non-null only when this process owns the database lifecycle (local/dev).
    private readonly PostgreSqlContainer? _postgres;

    // The connection string the host is configured with, known after InitializeAsync.
    private string _connectionString;

    public FilerApiFactory()
    {
        string? external = Environment.GetEnvironmentVariable(ExternalConnectionEnvVar);

        if (string.IsNullOrWhiteSpace(external))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:17")
                .Build();
            _connectionString = string.Empty; // filled in once the container starts
        }
        else
        {
            _connectionString = external;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Added after the host's appsettings sources, so these win. Supplies the
        // resolved Postgres plus a self-contained JWT config (the signing key is
        // test-only and ≥32 chars to satisfy JwtOptions validation — 05-security.md).
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _connectionString,
                ["Jwt:Issuer"] = "filer-test",
                ["Jwt:Audience"] = "filer-test-clients",
                ["Jwt:SigningKey"] = "filer-integration-test-signing-key-which-is-long-enough",
                ["Jwt:AccessTokenMinutes"] = "15",
            });
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
            _connectionString = _postgres.GetConnectionString();
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
