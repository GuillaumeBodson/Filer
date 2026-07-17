using System.Net;
using System.Text;
using Filer.ApiClient.Auth;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Auth;

public sealed class BearerTokenHandlerTests
{
    private static readonly Uri ApiBase = new("https://api.test/");

    private static TokenPair Tokens(string access = "access-1", string refresh = "refresh-1") =>
        new(access, DateTimeOffset.MaxValue, refresh, DateTimeOffset.MaxValue);

    private static HttpRequestMessage Request() =>
        new(HttpMethod.Get, "https://api.test/api/v1/documents");

    private static async Task<HttpResponseMessage> SendAsync(
        BearerTokenHandler handler, StubHttpMessageHandler inner, HttpRequestMessage request)
    {
        handler.InnerHandler = inner;
        using var invoker = new HttpMessageInvoker(handler);
        return await invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Attaches_bearer_token_when_present()
    {
        var store = new FakeTokenStore(Tokens());
        var refresher = new FakeTokenRefresher(store, succeeds: false);
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK);

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        await SendAsync(handler, inner, Request());

        inner.Requests.Should().ContainSingle();
        inner.Requests[0].BearerToken.Should().Be("access-1");
    }

    [Fact]
    public async Task Sends_without_authorization_when_signed_out()
    {
        var store = new FakeTokenStore(initial: null);
        var refresher = new FakeTokenRefresher(store, succeeds: false);
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK);

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        await SendAsync(handler, inner, Request());

        inner.Requests.Should().ContainSingle();
        inner.Requests[0].BearerToken.Should().BeNull();
        refresher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task On_401_refreshes_and_retries_with_the_rotated_token()
    {
        var store = new FakeTokenStore(Tokens());
        var refresher = new FakeTokenRefresher(store, succeeds: true, rotatedAccessToken: "access-2");
        var inner = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.Unauthorized)
            .Enqueue(HttpStatusCode.OK);

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        var response = await SendAsync(handler, inner, Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        refresher.CallCount.Should().Be(1);
        inner.Requests.Should().HaveCount(2);
        inner.Requests[0].BearerToken.Should().Be("access-1");
        inner.Requests[1].BearerToken.Should().Be("access-2");
    }

    [Fact]
    public async Task On_401_with_failed_refresh_clears_session_and_returns_401()
    {
        var store = new FakeTokenStore(Tokens());
        var refresher = new FakeTokenRefresher(store, succeeds: false);
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized);

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        var response = await SendAsync(handler, inner, Request());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refresher.CallCount.Should().Be(1);
        store.ClearCount.Should().Be(1);
        store.Current.Should().BeNull();
        inner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task On_401_with_transient_refresh_failure_keeps_the_session_and_returns_401()
    {
        // A 5xx during refresh is not a dead session (#167): tokens stay, the original
        // 401 surfaces, and no retry is attempted with a token we know is stale.
        var store = new FakeTokenStore(Tokens());
        var refresher = new FakeTokenRefresher(store, TokenRefreshResult.TransientFailure);
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized);

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        var response = await SendAsync(handler, inner, Request());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refresher.CallCount.Should().Be(1);
        store.ClearCount.Should().Be(0);
        store.Current.Should().NotBeNull();
        inner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Does_not_refresh_when_there_is_no_refresh_token()
    {
        var store = new FakeTokenStore(new TokenPair("access-1", null, RefreshToken: "", null));
        var refresher = new FakeTokenRefresher(store, succeeds: true);
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized);

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        var response = await SendAsync(handler, inner, Request());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refresher.CallCount.Should().Be(0);
        inner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Second_401_after_a_successful_refresh_returns_401_without_another_refresh()
    {
        var store = new FakeTokenStore(Tokens());
        var refresher = new FakeTokenRefresher(store, succeeds: true, rotatedAccessToken: "access-2");
        var inner = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.Unauthorized)
            .Enqueue(HttpStatusCode.Unauthorized);

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        var response = await SendAsync(handler, inner, Request());

        // The "no infinite loop" guarantee: one refresh, one retry, then the 401 surfaces.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refresher.CallCount.Should().Be(1);
        inner.Requests.Should().HaveCount(2);
        inner.Requests[1].BearerToken.Should().Be("access-2");
    }

    [Fact]
    public async Task Retry_after_refresh_preserves_the_request_body_and_content_headers()
    {
        var store = new FakeTokenStore(Tokens());
        var refresher = new FakeTokenRefresher(store, succeeds: true, rotatedAccessToken: "access-2");
        var inner = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.Unauthorized)
            .Enqueue(HttpStatusCode.OK);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.test/api/v1/folders")
        {
            Content = new StringContent("""{ "name": "Taxes" }""", Encoding.UTF8, "application/json"),
        };

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        var response = await SendAsync(handler, inner, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Requests.Should().HaveCount(2);
        inner.Requests[1].Body.Should().Equal(inner.Requests[0].Body);
        inner.Requests[1].ContentType.Should().Be(inner.Requests[0].ContentType).And.NotBeNull();
        inner.Requests[1].BearerToken.Should().Be("access-2");
    }

    [Theory]
    [InlineData("https://evil.test/api/v1/documents")] // different host
    [InlineData("http://api.test/api/v1/documents")]   // different scheme
    [InlineData("https://api.test:8443/api/v1/documents")] // different port
    public async Task Foreign_origin_never_receives_the_bearer_token(string foreignUri)
    {
        // Defense in depth (#168): even a pre-set Authorization header is stripped
        // when the request leaves the configured API origin.
        var store = new FakeTokenStore(Tokens());
        var refresher = new FakeTokenRefresher(store, succeeds: true);
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK);
        var request = new HttpRequestMessage(HttpMethod.Get, foreignUri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "leaked");

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        await SendAsync(handler, inner, request);

        inner.Requests.Should().ContainSingle();
        inner.Requests[0].BearerToken.Should().BeNull();
    }

    [Fact]
    public async Task Foreign_origin_401_does_not_trigger_refresh_or_clear_the_session()
    {
        var store = new FakeTokenStore(Tokens());
        var refresher = new FakeTokenRefresher(store, succeeds: true);
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized);

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        var response = await SendAsync(handler, inner,
            new HttpRequestMessage(HttpMethod.Get, "https://evil.test/login"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refresher.CallCount.Should().Be(0);
        store.ClearCount.Should().Be(0);
    }

    [Fact]
    public async Task Non_401_response_passes_through_untouched()
    {
        var store = new FakeTokenStore(Tokens());
        var refresher = new FakeTokenRefresher(store, succeeds: true);
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.BadRequest);

        using var handler = new BearerTokenHandler(store, refresher, ApiBase);
        var response = await SendAsync(handler, inner, Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refresher.CallCount.Should().Be(0);
    }

    private sealed class FakeTokenRefresher : ITokenRefresher
    {
        private readonly FakeTokenStore _store;
        private readonly TokenRefreshResult _result;
        private readonly string _rotatedAccessToken;

        public FakeTokenRefresher(FakeTokenStore store, bool succeeds, string rotatedAccessToken = "access-2")
            : this(store, succeeds ? TokenRefreshResult.Refreshed : TokenRefreshResult.Rejected, rotatedAccessToken)
        {
        }

        public FakeTokenRefresher(FakeTokenStore store, TokenRefreshResult result, string rotatedAccessToken = "access-2")
        {
            _store = store;
            _result = result;
            _rotatedAccessToken = rotatedAccessToken;
        }

        public int CallCount { get; private set; }

        public async Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_result != TokenRefreshResult.Refreshed)
            {
                return _result;
            }

            await _store.SaveAsync(
                new TokenPair(_rotatedAccessToken, DateTimeOffset.MaxValue, "refresh-2", DateTimeOffset.MaxValue),
                cancellationToken);
            return TokenRefreshResult.Refreshed;
        }
    }
}
