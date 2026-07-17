using System.Net;
using System.Text;
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

    [Fact]
    public async Task Refresh_response_missing_a_token_returns_false_and_stores_nothing()
    {
        var store = new FakeTokenStore(new TokenPair("old-access", null, "old-refresh", null));
        var inner = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.OK,
            """{ "accessToken": "new-access", "tokenType": "Bearer" }""");
        using var refresher = new TokenRefresher(
            new SingleClientFactory(inner), store, BaseAddress, "FilerApiAuth");

        bool result = await refresher.TryRefreshAsync(TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        store.SaveCount.Should().Be(0);
        store.Current!.AccessToken.Should().Be("old-access");
    }

    [Fact]
    public async Task Concurrent_refreshes_are_single_flight_and_share_one_round_trip()
    {
        var store = new FakeTokenStore(new TokenPair("old-access", null, "old-refresh", null));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new StubHttpMessageHandler().Enqueue(async _ =>
        {
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RefreshJson, Encoding.UTF8, "application/json"),
            };
        });
        using var refresher = new TokenRefresher(
            new SingleClientFactory(inner), store, BaseAddress, "FilerApiAuth");

        // The first caller acquires the gate and is now awaiting the gated response;
        // the second queues up behind the semaphore.
        Task<bool> first = refresher.TryRefreshAsync(TestContext.Current.CancellationToken);
        Task<bool> second = refresher.TryRefreshAsync(TestContext.Current.CancellationToken);
        release.SetResult();

        (await first).Should().BeTrue();
        (await second).Should().BeTrue();
        inner.Requests.Should().ContainSingle();
        store.SaveCount.Should().Be(1);
        store.Current!.AccessToken.Should().Be("new-access");
    }

    [Fact]
    public async Task Already_rotated_pair_returns_true_without_a_round_trip()
    {
        // The store hands out a different pair on the second read, as if another caller
        // rotated the tokens while this one waited on the gate.
        var store = new ScriptedTokenStore(
            new TokenPair("old-access", null, "old-refresh", null),
            new TokenPair("new-access", null, "new-refresh", null));
        var inner = new StubHttpMessageHandler();
        using var refresher = new TokenRefresher(
            new SingleClientFactory(inner), store, BaseAddress, "FilerApiAuth");

        bool result = await refresher.TryRefreshAsync(TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        inner.Requests.Should().BeEmpty();
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
    }

    /// <summary>Returns the given pairs on successive reads (the last one repeats).</summary>
    private sealed class ScriptedTokenStore(params TokenPair?[] reads) : ITokenStore
    {
        private int _index;

        public event EventHandler? Changed { add { } remove { } }

        public Task<TokenPair?> GetAsync(CancellationToken cancellationToken = default)
        {
            TokenPair? pair = reads[Math.Min(_index, reads.Length - 1)];
            _index++;
            return Task.FromResult(pair);
        }

        public Task SaveAsync(TokenPair tokens, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This scenario must not save.");

        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This scenario must not clear.");
    }
}
