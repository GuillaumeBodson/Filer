using System.Net;
using Filer.ApiClient.Auth;
using Filer.ApiClient.Generated;
using Filer.Ui.Auth;
using Filer.Ui.Models;
using FluentAssertions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Filer.Ui.Tests.Auth;

/// <summary>
/// Exercises <see cref="AuthSession"/> through the real Kiota client against a stubbed
/// transport, so the declared error mapping (#146) and the problem-details contract
/// (#169, code extension) are covered end to end.
/// </summary>
public sealed class AuthSessionTests
{
    private const string LoginJson = """
    {
      "accessToken": "access-1",
      "expiresAt": "2099-01-01T00:00:00+00:00",
      "refreshToken": "refresh-1",
      "refreshTokenExpiresAt": "2099-02-01T00:00:00+00:00",
      "tokenType": "Bearer"
    }
    """;

    private const string InvalidCredentialsJson = """
    {
      "type": "https://docs/errors/invalid_credentials",
      "title": "Authentication failed",
      "status": 401,
      "detail": "Invalid email or password.",
      "code": "invalid_credentials"
    }
    """;

    private static AuthSession CreateSession(StubHttpMessageHandler handler, FakeTokenStore store)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.test/"),
        };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient)
        {
            BaseUrl = "https://api.test/",
        };
        return new AuthSession(new FilerApiClient(adapter), store);
    }

    [Fact]
    public async Task Login_stores_the_issued_pair_and_returns_no_problem()
    {
        var store = new FakeTokenStore();
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, LoginJson);
        AuthSession session = CreateSession(inner, store);

        ProblemDetailsView? problem = await session.LoginAsync(
            "user@example.com", "s3cure-pass", TestContext.Current.CancellationToken);

        problem.Should().BeNull();
        store.Current!.AccessToken.Should().Be("access-1");
        store.Current.RefreshToken.Should().Be("refresh-1");
        inner.Requests.Should().ContainSingle();
        inner.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v1/auth/login");
    }

    [Fact]
    public async Task Rejected_login_surfaces_the_problem_with_the_machine_code()
    {
        var store = new FakeTokenStore();
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized, InvalidCredentialsJson);
        AuthSession session = CreateSession(inner, store);

        ProblemDetailsView? problem = await session.LoginAsync(
            "user@example.com", "wrong", TestContext.Current.CancellationToken);

        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Authentication failed");
        problem.Detail.Should().Be("Invalid email or password.");
        problem.Status.Should().Be(401);
        problem.Code.Should().Be("invalid_credentials");
        store.Current.Should().BeNull();
        store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Register_then_signs_in_with_the_same_credentials()
    {
        var store = new FakeTokenStore();
        var inner = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.Created, """{ "id": "11111111-1111-1111-1111-111111111111", "email": "new@example.com" }""")
            .Enqueue(HttpStatusCode.OK, LoginJson);
        AuthSession session = CreateSession(inner, store);

        ProblemDetailsView? problem = await session.RegisterAsync(
            "new@example.com", "s3cure-pass", TestContext.Current.CancellationToken);

        problem.Should().BeNull();
        inner.Requests.Should().HaveCount(2);
        inner.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v1/auth/register");
        inner.Requests[1].RequestUri!.AbsolutePath.Should().Be("/api/v1/auth/login");
        store.Current!.AccessToken.Should().Be("access-1");
    }

    [Fact]
    public async Task A_taken_email_stops_before_the_login_call()
    {
        var store = new FakeTokenStore();
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Conflict, """
        {
          "type": "https://docs/errors/email_taken",
          "title": "Conflict",
          "status": 409,
          "detail": "An account with this email already exists.",
          "code": "email_taken"
        }
        """);
        AuthSession session = CreateSession(inner, store);

        ProblemDetailsView? problem = await session.RegisterAsync(
            "taken@example.com", "s3cure-pass", TestContext.Current.CancellationToken);

        problem!.Code.Should().Be("email_taken");
        inner.Requests.Should().ContainSingle();
        store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_clears_the_store()
    {
        var store = new FakeTokenStore(new TokenPair("access-1", null, "refresh-1", null));
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.NoContent);
        AuthSession session = CreateSession(inner, store);

        await session.LogoutAsync(TestContext.Current.CancellationToken);

        inner.Requests.Should().ContainSingle();
        inner.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v1/auth/logout");
        System.Text.Encoding.UTF8.GetString(inner.Requests[0].Body!).Should().Contain("refresh-1");
        store.Current.Should().BeNull();
        store.ClearCount.Should().Be(1);
    }

    [Fact]
    public async Task Logout_clears_the_store_even_when_revocation_fails()
    {
        var store = new FakeTokenStore(new TokenPair("access-1", null, "refresh-1", null));
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.InternalServerError);
        AuthSession session = CreateSession(inner, store);

        await session.LogoutAsync(TestContext.Current.CancellationToken);

        store.Current.Should().BeNull();
        store.ClearCount.Should().Be(1);
    }

    [Fact]
    public async Task Signed_out_logout_skips_the_server_and_still_clears()
    {
        var store = new FakeTokenStore(initial: null);
        var inner = new StubHttpMessageHandler();
        AuthSession session = CreateSession(inner, store);

        await session.LogoutAsync(TestContext.Current.CancellationToken);

        inner.Requests.Should().BeEmpty();
        store.ClearCount.Should().Be(1);
    }

    [Fact]
    public async Task Profile_returns_the_current_user()
    {
        var store = new FakeTokenStore(new TokenPair("access-1", null, "refresh-1", null));
        var inner = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{ "id": "11111111-1111-1111-1111-111111111111", "email": "user@example.com" }""");
        AuthSession session = CreateSession(inner, store);

        ProfileResult result = await session.GetProfileAsync(TestContext.Current.CancellationToken);

        result.Problem.Should().BeNull();
        result.Profile!.Email.Should().Be("user@example.com");
    }
}
