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
    /// <summary>
    /// Adds the typed Filer API client to the container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseAddress">The API base address, e.g. <c>https://api.example.com/</c>.</param>
    public static IServiceCollection AddFilerApiClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        // Anonymous for now: bearer-token acquisition, storage and 401 refresh land in
        // #128, which replaces this registration with an authenticating provider.
        services.AddSingleton<IAuthenticationProvider, AnonymousAuthenticationProvider>();

        services.AddScoped<IRequestAdapter>(sp =>
            new HttpClientRequestAdapter(sp.GetRequiredService<IAuthenticationProvider>())
            {
                BaseUrl = baseAddress.ToString(),
            });

        services.AddScoped<FilerApiClient>();

        return services;
    }
}
