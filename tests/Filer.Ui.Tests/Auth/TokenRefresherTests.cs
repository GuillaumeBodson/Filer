using System.Net;
using Filer.ApiClient.Auth;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Auth;

public sealed class TokenRefresherTests
{
    private static readonly Uri BaseAddress = new("https://api.test/");

    private const string RefreshJson = """
    {
      "accessToken": "new-access",
      "expiresAt": "2099-01-01T00:00:00+00:00",
      "refreshToken": "new-refresh",
      "refreshTokenExpiresAt": "2099-02-01T00:00:00+00:00",
      "tokenType": "Bearer"
    }
    """;

    [Fact]
    public async Task Refresh_stores_the_rotated_pair_and_returns_true()
    {
        var store = new FakeTokenStore(new TokenPair("old-access", null, "old-refresh", null));
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, RefreshJson);
        using var refresher = new TokenRefresher(
            new SingleClientFactory(inner), store, BaseAddress, "FilerApiAuth");

        bool result = await refresher.TryRefreshAsync(TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        store.Current.Should().NotBeNull();
        store.Current!.AccessToken.Should().Be("new-access");
        store.Current.RefreshToken.Should().Be("new-refresh");
        inner.Requests.Should().ContainSingle();
        inner.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v1/auth/refresh");
    }

    [Fact]
    public async Task Rejected_refresh_token_returns_false_and_keeps_old_tokens()
    {
        var store = new FakeTokenStore(new TokenPair("old-access", null, "old-refresh", null));
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized);
        using var refresher = new TokenRefresher(
            new SingleClientFactory(inner), store, BaseAddress, "FilerApiAuth");

        bool result = await refresher.TryRefreshAsync(TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        store.Current!.AccessToken.Should().Be("old-access");
        store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task No_refresh_token_returns_false_without_calling_the_server()
    {
        var store = new FakeTokenStore(initial: null);
        var inner = new StubHttpMessageHandler();
        using var refresher = new TokenRefresher(
            new SingleClientFactory(inner), store, BaseAddress, "FilerApiAuth");

        bool result = await refresher.TryRefreshAsync(TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        inner.Requests.Should().BeEmpty();
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
    }
}
