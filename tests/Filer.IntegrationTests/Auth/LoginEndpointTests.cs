using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.Auth.Contracts;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Filer.IntegrationTests.Auth;

/// <summary>
/// POST /api/v1/auth/login. A wrong password and an unknown email must be
/// indistinguishable to the caller — both return the same generic 401 so the API
/// never reveals whether an account exists (05-security.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class LoginEndpointTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task Login_WithValidCredentials_Returns200WithBearerToken()
    {
        HttpClient client = _factory.CreateClient();
        TestData.RegisterRequest account = TestData.NewRegistration();
        (await client.RegisterAsync(account)).EnsureSuccessStatusCode();

        HttpResponseMessage response = await client.LoginAsync(account.Email, account.Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LoginResult? token = await response.Content.ReadFromJsonAsync<LoginResult>(CancellationToken.None);
        token.Should().NotBeNull();
        token!.AccessToken.Should().NotBeNullOrWhiteSpace();
        token.TokenType.Should().Be("Bearer");
        token.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        token.RefreshToken.Should().NotBeNullOrWhiteSpace();
        token.RefreshTokenExpiresAt.Should().BeAfter(token.ExpiresAt);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401InvalidCredentials()
    {
        HttpClient client = _factory.CreateClient();
        TestData.RegisterRequest account = TestData.NewRegistration();
        (await client.RegisterAsync(account)).EnsureSuccessStatusCode();

        HttpResponseMessage response = await client.LoginAsync(account.Email, "WrongPassword!1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);
        problem!.Title.Should().Be(AuthErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Returns401InvalidCredentials()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.LoginAsync(TestData.UniqueEmail(), TestData.ValidPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);
        problem!.Title.Should().Be(AuthErrorCodes.InvalidCredentials);
    }
}
