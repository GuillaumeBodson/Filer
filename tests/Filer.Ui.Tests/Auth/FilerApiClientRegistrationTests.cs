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
