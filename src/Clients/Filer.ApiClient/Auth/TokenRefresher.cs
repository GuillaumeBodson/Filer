using Filer.ApiClient.Generated;
using Filer.ApiClient.Generated.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Filer.ApiClient.Auth;

/// <summary>
/// Calls <c>/auth/refresh</c> through a dedicated, bearer-handler-free HTTP client so
/// the refresh itself never recurses into <see cref="BearerTokenHandler"/>'s 401 retry.
/// Concurrent refreshes are serialized; a caller that arrives after another already
/// rotated the pair returns success without a second network round-trip.
/// </summary>
public sealed class TokenRefresher(
    IHttpClientFactory httpClientFactory,
    ITokenStore tokenStore,
    Uri baseAddress,
    string authHttpClientName) : ITokenRefresher, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ITokenStore _tokenStore = tokenStore;
    private readonly Uri _baseAddress = baseAddress;
    private readonly string _authHttpClientName = authHttpClientName;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
    {
        TokenPair? before = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
        if (before?.RefreshToken is null)
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A concurrent caller may have rotated the pair while we waited.
            TokenPair? current = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            if (current?.RefreshToken is null)
            {
                return false;
            }

            if (!string.Equals(current.AccessToken, before.AccessToken, StringComparison.Ordinal))
            {
                return true;
            }

            FilerApiClient client = CreateAnonymousClient();
            try
            {
                RefreshResponse? response = await client.Api.V1.Auth.Refresh
                    .PostAsync(new RefreshRequest { RefreshToken = current.RefreshToken }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (response?.AccessToken is null || response.RefreshToken is null)
                {
                    return false;
                }

                await _tokenStore.SaveAsync(
                    new TokenPair(
                        response.AccessToken,
                        response.ExpiresAt,
                        response.RefreshToken,
                        response.RefreshTokenExpiresAt),
                    cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch (ApiException)
            {
                // Invalid/expired/reused refresh token (401) - caller ends the session.
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private FilerApiClient CreateAnonymousClient()
    {
        HttpClient httpClient = _httpClientFactory.CreateClient(_authHttpClientName);
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient)
        {
            BaseUrl = _baseAddress.ToString(),
        };
        return new FilerApiClient(adapter);
    }

    public void Dispose() => _gate.Dispose();
}
