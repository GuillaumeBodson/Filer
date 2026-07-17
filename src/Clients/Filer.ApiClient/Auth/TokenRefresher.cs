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

    public async Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        TokenPair? before = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
        if (before?.RefreshToken is null)
        {
            return TokenRefreshResult.Rejected;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A concurrent caller may have rotated the pair while we waited.
            TokenPair? current = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
            if (current?.RefreshToken is null)
            {
                return TokenRefreshResult.Rejected;
            }

            if (!string.Equals(current.AccessToken, before.AccessToken, StringComparison.Ordinal))
            {
                return TokenRefreshResult.Refreshed;
            }

            FilerApiClient client = CreateAnonymousClient();
            try
            {
                RefreshResponse? response = await client.Api.V1.Auth.Refresh
                    .PostAsync(new RefreshRequest { RefreshToken = current.RefreshToken }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (response?.AccessToken is null || response.RefreshToken is null)
                {
                    // A 2xx without a usable pair is a server fault, not a verdict on
                    // the refresh token - don't end the session over it.
                    return TokenRefreshResult.TransientFailure;
                }

                await _tokenStore.SaveAsync(
                    new TokenPair(
                        response.AccessToken,
                        response.ExpiresAt,
                        response.RefreshToken,
                        response.RefreshTokenExpiresAt),
                    cancellationToken).ConfigureAwait(false);

                return TokenRefreshResult.Refreshed;
            }
            catch (ApiException ex) when (ex.ResponseStatusCode is 401 or 403)
            {
                // Invalid/expired/reused refresh token - the caller ends the session.
                return TokenRefreshResult.Rejected;
            }
            catch (ApiException)
            {
                // 5xx / gateway timeout during a deploy: the token was never judged.
                // Keep the session; a later request retries the refresh (#167).
                return TokenRefreshResult.TransientFailure;
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
