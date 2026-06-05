using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Filer.IntegrationTests.Infrastructure;
using Xunit;

namespace Filer.IntegrationTests.Auth;

/// <summary>
/// GET /api/v1/auth/me — a protected endpoint. Unauthenticated access to a
/// protected endpoint must return 401 (12-testing-strategy.md, security-critical),
/// and a valid token must surface the caller's own identity from its claims.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MeEndpointTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/auth/me", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithMalformedToken_Returns401()
    {
        HttpClient client = _factory.CreateClient().WithBearer("this.is.not-a-valid-jwt");

        HttpResponseMessage response = await client.GetAsync("/api/v1/auth/me", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsCallersProfile()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/v1/auth/me", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MeResult? me = await response.Content.ReadFromJsonAsync<MeResult>(CancellationToken.None);
        me.Should().NotBeNull();
        me!.Id.Should().Be(user.Id);
        me.Email.Should().Be(user.Email);
    }
}
