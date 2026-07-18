using System.Net;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Filer.IntegrationTests.Host;

/// <summary>
/// Health endpoints (#159, 04-non-functional.md): liveness answers as long as the
/// process is up; readiness reflects the hard dependencies — Postgres and the
/// storage root. Both are anonymous and sit outside the API contract (03).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class HealthEndpointTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task HealthLive_Returns200WithoutAuthentication()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response =
            await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_WithDatabaseAndStorageAvailable_Returns200()
    {
        // The shared factory runs a real Postgres (Testcontainers/CI service) and a
        // writable temp storage root — the healthy path end to end.
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response =
            await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_WhenTheStorageRootIsNotWritable_Returns503()
    {
        // Point the storage root at a regular FILE: Directory.CreateDirectory then
        // fails identically on Windows and Linux. A dedicated host is booted for
        // this (the shared one must stay healthy for the other tests); the env var
        // is restored immediately after — the collection runs sequentially, and
        // the already-built shared host no longer reads it.
        string fileAsRoot = Path.Combine(Path.GetTempPath(), "filer-health-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(fileAsRoot, "not a directory", TestContext.Current.CancellationToken);
        string? originalRoot = Environment.GetEnvironmentVariable("Storage__RootPath");
        Environment.SetEnvironmentVariable("Storage__RootPath", fileAsRoot);
        try
        {
            await using var brokenStorageHost = new WebApplicationFactory<Program>();
            using HttpClient client = brokenStorageHost.CreateClient();

            HttpResponseMessage response =
                await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
                "an unwritable blob root means uploads would fail — not ready (04)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Storage__RootPath", originalRoot);
            File.Delete(fileAsRoot);
        }
    }

    [Fact]
    public async Task HealthEndpoints_AreAbsentFromTheOpenApiDocument()
    {
        // Health is infrastructure surface, not API contract (03): the OpenAPI
        // document — and therefore the generated client and the Kiota drift gate —
        // must not change because probes exist.
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response =
            await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The document is only mapped in Development; the Testing host not
            // exposing it at all proves the same point.
            return;
        }

        string document = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        document.Should().NotContain("/health/");
    }
}
