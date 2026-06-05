using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Filer.IntegrationTests.Infrastructure;
using Xunit;

namespace Filer.IntegrationTests.Auth;

/// <summary>
/// POST /api/v1/auth/logout — a protected endpoint that revokes the caller's
/// refresh-token family. Unauthenticated access is 401; a valid logout returns 204
/// and the revoked token can no longer be refreshed (issue #27 acceptance,
/// 05-security.md). Logout is idempotent and never reveals whether a token existed.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class LogoutEndpointTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    private static async Task<LoginResult> RegisterAndLoginAsync(HttpClient client)
    {
        TestData.RegisterRequest account = TestData.NewRegistration();
        (await client.RegisterAsync(account)).EnsureSuccessStatusCode();
        HttpResponseMessage login = await client.LoginAsync(account.Email, account.Password);
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResult>(CancellationToken.None))!;
    }

    [Fact]
    public async Task Logout_WithoutToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.LogoutAsync("some-refresh-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithValidToken_Returns204()
    {
        HttpClient client = _factory.CreateClient();
        LoginResult issued = await RegisterAndLoginAsync(client);
        client.WithBearer(issued.AccessToken);

        HttpResponseMessage response = await client.LogoutAsync(issued.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_ThenRefreshWithRevokedToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();
        LoginResult issued = await RegisterAndLoginAsync(client);
        client.WithBearer(issued.AccessToken);

        (await client.LogoutAsync(issued.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The refresh token was revoked by logout, so it can no longer be exchanged.
        HttpResponseMessage refresh = await client.RefreshAsync(issued.RefreshToken);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithMissingRefreshToken_Returns400()
    {
        HttpClient client = _factory.CreateClient();
        LoginResult issued = await RegisterAndLoginAsync(client);
        client.WithBearer(issued.AccessToken);

        HttpResponseMessage response = await client.LogoutAsync(string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Logout_IsIdempotent_RepeatedCallStillReturns204()
    {
        HttpClient client = _factory.CreateClient();
        LoginResult issued = await RegisterAndLoginAsync(client);
        client.WithBearer(issued.AccessToken);

        (await client.LogoutAsync(issued.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Logging out again with the same (now revoked) token is a harmless no-op.
        HttpResponseMessage second = await client.LogoutAsync(issued.RefreshToken);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
