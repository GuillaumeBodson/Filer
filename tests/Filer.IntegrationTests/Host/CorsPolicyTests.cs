using FluentAssertions;
using Filer.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Filer.IntegrationTests.Host;

/// <summary>
/// The CORS policy is configuration-driven (#148, 05-security.md): origins listed in
/// <c>Cors:AllowedOrigins</c> get the CORS headers, everything else - including the
/// default configuration-less host - gets none. No wildcard, no credentials.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CorsPolicyTests(FilerApiFactory factory)
{
    private const string AllowedOrigin = "http://spa.test";

    private readonly FilerApiFactory _factory = factory;

    private HttpClient CreateClientWithCorsConfigured() =>
        _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin)).CreateClient();

    [Fact]
    public async Task Preflight_from_a_configured_origin_is_allowed()
    {
        HttpClient client = CreateClientWithCorsConfigured();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        preflight.Headers.Add("Origin", AllowedOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        HttpResponseMessage response = await client.SendAsync(preflight, TestContext.Current.CancellationToken);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle(AllowedOrigin);
        string allowedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
        allowedHeaders.Should().ContainEquivalentOf("authorization");
    }

    [Fact]
    public async Task Preflight_from_an_unlisted_origin_gets_no_cors_headers()
    {
        HttpClient client = CreateClientWithCorsConfigured();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        preflight.Headers.Add("Origin", "https://evil.test");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");

        HttpResponseMessage response = await client.SendAsync(preflight, TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Without_configured_origins_the_middleware_is_off()
    {
        // The shared factory carries no Cors configuration - the same-origin
        // deployment default (05-security.md).
        HttpClient client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tags");
        request.Headers.Add("Origin", AllowedOrigin);

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
