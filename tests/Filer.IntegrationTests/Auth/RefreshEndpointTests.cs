using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.Auth.Contracts;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Filer.IntegrationTests.Auth;

/// <summary>
/// POST /api/v1/auth/refresh. A valid refresh token is exchanged for a new pair and
/// rotated out; reusing a rotated token is rejected as theft, and an unknown token
/// is a generic 401 — the API never reveals why (05-security.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RefreshEndpointTests(FilerApiFactory factory)
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
    public async Task Refresh_WithValidToken_Returns200WithRotatedPair()
    {
        HttpClient client = _factory.CreateClient();
        LoginResult issued = await RegisterAndLoginAsync(client);

        HttpResponseMessage response = await client.RefreshAsync(issued.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RefreshResult? rotated = await response.Content.ReadFromJsonAsync<RefreshResult>(CancellationToken.None);
        rotated.Should().NotBeNull();
        rotated!.AccessToken.Should().NotBeNullOrWhiteSpace();
        rotated.TokenType.Should().Be("Bearer");
        rotated.RefreshToken.Should().NotBeNullOrWhiteSpace()
            .And.Subject.Should().NotBe(issued.RefreshToken, "rotation issues a new refresh token");

        // The freshly issued access token authenticates against a protected endpoint.
        client.WithBearer(rotated.AccessToken);
        HttpResponseMessage me = await client.GetAsync("/api/v1/auth/me", CancellationToken.None);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_WhenRotatedTokenReused_Returns401()
    {
        HttpClient client = _factory.CreateClient();
        LoginResult issued = await RegisterAndLoginAsync(client);

        // First refresh consumes the original token and issues a successor.
        (await client.RefreshAsync(issued.RefreshToken)).EnsureSuccessStatusCode();

        // Replaying the now-consumed original is theft: it must be rejected.
        HttpResponseMessage replay = await client.RefreshAsync(issued.RefreshToken);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ProblemDetails? problem = await replay.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);
        problem!.Title.Should().Be(AuthErrorCodes.InvalidRefreshToken);
    }

    [Fact]
    public async Task Refresh_AfterReuse_RevokesTheWholeFamily()
    {
        HttpClient client = _factory.CreateClient();
        LoginResult issued = await RegisterAndLoginAsync(client);

        // Rotate once to obtain a legitimate successor, then replay the original.
        HttpResponseMessage firstRefresh = await client.RefreshAsync(issued.RefreshToken);
        firstRefresh.EnsureSuccessStatusCode();
        RefreshResult successor = (await firstRefresh.Content.ReadFromJsonAsync<RefreshResult>(CancellationToken.None))!;

        (await client.RefreshAsync(issued.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The reuse revoked the entire family, so the legitimate successor is dead too.
        HttpResponseMessage successorAttempt = await client.RefreshAsync(successor.RefreshToken);
        successorAttempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.RefreshAsync("not-a-real-refresh-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);
        problem!.Title.Should().Be(AuthErrorCodes.InvalidRefreshToken);
    }

    [Fact]
    public async Task Refresh_WithMissingToken_Returns400()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.RefreshAsync(string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
