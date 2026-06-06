using Microsoft.AspNetCore.Mvc.Testing;
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
///
/// <para>
/// Configuration is supplied through <b>environment variables</b>, deliberately not
/// through <c>ConfigureAppConfiguration</c>. The Auth module reads the <c>Jwt</c>
/// section and the Postgres connection string <i>eagerly</i> inside
/// <c>AddAuthModule</c>, which runs while <c>Program</c> is still configuring the
/// builder — before <see cref="WebApplicationFactory{TEntryPoint}"/> merges any
/// <c>ConfigureAppConfiguration</c> source. Environment variables are part of the
/// default configuration <c>CreateBuilder</c> reads up front, so they are visible to
/// that eager read; in-memory sources are not. The signing key is a test-only value
/// of ≥32 chars (satisfies <c>JwtOptions</c> validation — 05-security.md).
/// </para>
/// </summary>
public sealed class FilerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnectionEnvVar = "ConnectionStrings__Postgres";

    // Non-null only when this process owns the database lifecycle (local/dev).
    private readonly PostgreSqlContainer? _postgres;

    // Throwaway blob root for the Storage module; removed on dispose.
    private readonly string _storageRoot =
        Path.Combine(Path.GetTempPath(), "filer-it-storage-" + Guid.NewGuid().ToString("N"));

    public FilerApiFactory()
    {
        string? external = Environment.GetEnvironmentVariable(ConnectionEnvVar);
        if (string.IsNullOrWhiteSpace(external))
        {
            _postgres = new PostgreSqlBuilder("postgres:17")
                .Build();
        }

        // Set before the host is built (lazily, on first CreateClient — always after
        // this constructor). See the type remarks for why env vars, not in-memory.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "filer-test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "filer-test-clients");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "filer-integration-test-signing-key-which-is-long-enough");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        // Storage module: blobs go to a throwaway temp root, eagerly read by
        // AddStorageModule for the same reason as the Jwt section above.
        Environment.SetEnvironmentVariable("Storage__RootPath", _storageRoot);
        // BackgroundJobs: keep the hosted worker idle so queue/claim tests own the
        // table deterministically — the worker's orchestration is unit-tested at its
        // seams, the claim SQL is exercised here directly (12-testing-strategy.md).
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled", "false");
        // Documents: shrink the upload ceiling (prod default 50 MB, 04) so the
        // oversize→413 path is exercised with a 1 MB payload instead of a 50 MB
        // one; eagerly read by AddDocumentsModule like the sections above.
        Environment.SetEnvironmentVariable(
            "Documents__MaxUploadBytes", MaxUploadBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The upload size ceiling the host under test runs with (1 MB).</summary>
    public const long MaxUploadBytes = 1024 * 1024;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
            // Must be present as an env var before the host builds, for the same
            // eager-read reason as the JWT config. In CI the Postgres service already
            // exports this variable, so this branch is skipped.
            Environment.SetEnvironmentVariable(ConnectionEnvVar, _postgres.GetConnectionString());
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (_postgres is not null)
        {
            Environment.SetEnvironmentVariable(ConnectionEnvVar, null);
            await _postgres.DisposeAsync();
        }

        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }

        await base.DisposeAsync();
    }
}
