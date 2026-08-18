using System.Net;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Host;

/// <summary>
/// The web client ships in the api image and is served same-origin (ADR-019):
/// the root and any client-side route boot the WASM app via the SPA fallback,
/// while the API keeps its own contract — an unknown API route must stay a
/// problem-details 404 (03), never fall back to an HTML page that hides the
/// error from every non-browser caller.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class WebClientHostingTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task Root_ServesTheClientBootstrapPage()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response =
            await client.GetAsync("/", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.Should().Contain("blazor.webassembly.js",
            "the page served at the root must be the WASM bootstrap, not an error page");
    }

    [Fact]
    public async Task UnmatchedClientRoute_FallsBackToTheBootstrapPage()
    {
        // A deep link (e.g. a bookmarked /documents view) reaches the server, which
        // has no such endpoint; the fallback must hand the route to the WASM router.
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response =
            await client.GetAsync("/documents/some-client-route", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task UnknownApiRoute_StaysAProblemDetails404()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response =
            await client.GetAsync("/api/v1/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "the SPA fallback must never swallow an unknown API route into index.html (03)");
    }
}
