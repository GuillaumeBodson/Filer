using System.Net;
using Filer.ApiClient;
using Filer.ApiClient.Auth;
using Filer.ApiClient.Generated;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Ui.Tests.Auth;

/// <summary>
/// Pins the DI composition invariant behind the refresh design: the main client carries
/// <see cref="BearerTokenHandler"/>, while the auth client used by <see cref="TokenRefresher"/>
/// is handler-free so a refresh can neither recurse into the 401 retry nor leak a bearer
/// token onto /auth/refresh.
/// </summary>
public sealed class FilerApiClientRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITokenStore>(new FakeTokenStore());
        services.AddFilerApiClient(new Uri("https://api.test/"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void FilerApi_client_has_the_bearer_handler_in_its_chain()
    {
        using ServiceProvider provider = BuildProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        List<HttpMessageHandler> chain = HandlerChain(
            factory.CreateHandler(FilerApiClientServiceCollectionExtensions.ApiHttpClientName));

        chain.OfType<BearerTokenHandler>().Should().ContainSingle();
    }

    [Fact]
    public void FilerApiAuth_client_has_no_bearer_handler()
    {
        using ServiceProvider provider = BuildProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        List<HttpMessageHandler> chain = HandlerChain(
            factory.CreateHandler(FilerApiClientServiceCollectionExtensions.AuthHttpClientName));

        chain.OfType<BearerTokenHandler>().Should().BeEmpty();
    }

    [Fact]
    public void The_full_client_graph_resolves_once_the_host_supplies_a_token_store()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<FilerApiClient>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITokenRefresher>().Should().NotBeNull();
    }

    [Fact]
    public void TokenRefresher_is_one_instance_across_scopes()
    {
        // The single-flight gate ("concurrent refreshes are serialized") and the
        // handler-scope/app-scope split (#166) both require a singleton refresher.
        using ServiceProvider provider = BuildProvider();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        first.ServiceProvider.GetRequiredService<ITokenRefresher>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<ITokenRefresher>());
    }

    [Fact]
    public async Task A_refresh_through_the_factory_built_handler_chain_reaches_the_app_scope_store()
    {
        // End-to-end wiring for #166: IHttpClientFactory builds BearerTokenHandler in
        // its own DI scope. The store's Changed event raised by the refresh must still
        // reach a subscriber holding the app-scope ITokenStore - i.e. both sides must
        // observe the same instance.
        var services = new ServiceCollection();
        services.AddSingleton<ITokenStore>(new FakeTokenStore(
            new TokenPair("access-1", DateTimeOffset.MaxValue, "refresh-1", DateTimeOffset.MaxValue)));
        services.AddFilerApiClient(new Uri("https://api.test/"));

        // Stub the network on both named clients: the API returns 401 then 200; the
        // handler-free auth client answers /auth/refresh with a rotated pair.
        services.AddHttpClient(FilerApiClientServiceCollectionExtensions.ApiHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler()
                .Enqueue(HttpStatusCode.Unauthorized)
                .Enqueue(HttpStatusCode.OK));
        services.AddHttpClient(FilerApiClientServiceCollectionExtensions.AuthHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler()
                .Enqueue(HttpStatusCode.OK, """
                {
                  "accessToken": "access-2",
                  "expiresAt": "2099-01-01T00:00:00+00:00",
                  "refreshToken": "refresh-2",
                  "refreshTokenExpiresAt": "2099-01-01T00:00:00+00:00"
                }
                """));

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope appScope = provider.CreateScope();
        ITokenStore appStore = appScope.ServiceProvider.GetRequiredService<ITokenStore>();
        int changedRaised = 0;
        appStore.Changed += (_, _) => changedRaised++;

        HttpClient client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(FilerApiClientServiceCollectionExtensions.ApiHttpClientName);
        HttpResponseMessage response = await client.GetAsync(
            new Uri("https://api.test/api/v1/documents"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        changedRaised.Should().BeGreaterThan(0,
            "the refresh SaveAsync must be observed through the app-scope store instance");
        (await appStore.GetAsync(TestContext.Current.CancellationToken))!.AccessToken
            .Should().Be("access-2");
    }

    private static List<HttpMessageHandler> HandlerChain(HttpMessageHandler outermost)
    {
        var chain = new List<HttpMessageHandler> { outermost };
        HttpMessageHandler current = outermost;
        while (current is DelegatingHandler { InnerHandler: { } inner })
        {
            chain.Add(inner);
            current = inner;
        }

        return chain;
    }
}
