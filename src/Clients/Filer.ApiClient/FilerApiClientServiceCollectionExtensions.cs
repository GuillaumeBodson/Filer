using Filer.ApiClient.Auth;
using Filer.ApiClient.Generated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Filer.ApiClient;

/// <summary>
/// Registers the Kiota-generated <see cref="FilerApiClient"/> and its request adapter
/// (ADR-011). The base address is supplied by the host so each environment - and each
/// shell (Blazor WASM today, MAUI later, RM-02) - points the client at its own API.
/// </summary>
public static class FilerApiClientServiceCollectionExtensions
{
    /// <summary>Named HTTP client carrying the bearer/refresh handler chain.</summary>
    public const string ApiHttpClientName = "FilerApi";

    /// <summary>Named HTTP client without the bearer handler, used to call /auth/refresh.</summary>
    public const string AuthHttpClientName = "FilerApiAuth";

    /// <summary>
    /// Adds the typed Filer API client and its auth plumbing to the container. The host
    /// must also register an <see cref="ITokenStore"/> (e.g. browser localStorage in
    /// Filer.Web) that supplies and persists the tokens.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseAddress">The API base address, e.g. <c>https://api.example.com/</c>.</param>
    public static IServiceCollection AddFilerApiClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        // The bearer token is attached by BearerTokenHandler (which can also observe a
        // 401 and retry), so Kiota's own auth provider stays anonymous.
        services.AddSingleton<IAuthenticationProvider, AnonymousAuthenticationProvider>();

        services.AddTransient<BearerTokenHandler>();
        services.AddScoped<ITokenRefresher>(sp => new TokenRefresher(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ITokenStore>(),
            baseAddress,
            AuthHttpClientName));

        // Main client: every request flows through the bearer/refresh handler.
        services.AddHttpClient(ApiHttpClientName, client => client.BaseAddress = baseAddress)
            .AddHttpMessageHandler<BearerTokenHandler>();

        // Auth client: no bearer handler, so the refresher's /auth/refresh call does not
        // recurse through the 401 retry.
        services.AddHttpClient(AuthHttpClientName, client => client.BaseAddress = baseAddress);

        services.AddScoped<IRequestAdapter>(sp =>
        {
            HttpClient httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiHttpClientName);
            return new HttpClientRequestAdapter(sp.GetRequiredService<IAuthenticationProvider>(), httpClient: httpClient)
            {
                BaseUrl = baseAddress.ToString(),
            };
        });

        services.AddScoped<FilerApiClient>();

        return services;
    }
}
