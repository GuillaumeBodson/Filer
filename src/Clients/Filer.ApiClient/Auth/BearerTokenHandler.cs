using System.Net;
using System.Net.Http.Headers;

namespace Filer.ApiClient.Auth;

/// <summary>
/// Delegating handler on the API <see cref="HttpClient"/> that attaches the bearer
/// access token and, on a 401, refreshes once and retries the request (05-security.md,
/// ADR-014).
/// A <see cref="TokenRefreshResult.Rejected"/> refresh clears the session - the
/// store's <see cref="ITokenStore.Changed"/> event flips auth state so the app routes
/// the user to sign in. A <see cref="TokenRefreshResult.TransientFailure"/> keeps the
/// tokens: the original 401 surfaces and a later request retries the refresh (#167).
/// The token is only ever attached to requests targeting the configured API origin;
/// any other origin gets the request with the Authorization header stripped (#168).
/// </summary>
public sealed class BearerTokenHandler(
    ITokenStore tokenStore, ITokenRefresher tokenRefresher, Uri apiBaseAddress)
    : DelegatingHandler
{
    private readonly ITokenStore _tokenStore = tokenStore;
    private readonly ITokenRefresher _tokenRefresher = tokenRefresher;
    private readonly Uri _apiBaseAddress = apiBaseAddress;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Defense in depth (#168): the named-client BaseAddress plus Kiota's relative
        // URLs normally keep every request on the API origin, but an absolute URL to
        // a foreign origin must never carry the user's token - strip anything pre-set
        // and stay out of the refresh flow.
        if (!TargetsApiOrigin(request.RequestUri))
        {
            request.Headers.Authorization = null;
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        TokenPair? tokens = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
        ApplyBearer(request, tokens?.AccessToken);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Only a 401 with a refresh token in hand is worth retrying.
        if (response.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrEmpty(tokens?.RefreshToken))
        {
            return response;
        }

        TokenRefreshResult refreshed = await _tokenRefresher.RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (refreshed == TokenRefreshResult.Rejected)
        {
            // Refresh token is dead: end the session and surface the original 401.
            await _tokenStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            return response;
        }

        if (refreshed == TokenRefreshResult.TransientFailure)
        {
            // Server blip, not a verdict on the session: keep the tokens and surface
            // the original 401 - the next request retries the refresh.
            return response;
        }

        TokenPair? rotated = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
        HttpRequestMessage retry = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        ApplyBearer(retry, rotated?.AccessToken);

        response.Dispose();
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }

    private bool TargetsApiOrigin(Uri? requestUri) =>
        // A relative URI resolves against the named client's BaseAddress, which is
        // the API - only an absolute URI can point elsewhere.
        requestUri is not { IsAbsoluteUri: true }
        || (string.Equals(requestUri.Scheme, _apiBaseAddress.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(requestUri.Host, _apiBaseAddress.Host, StringComparison.OrdinalIgnoreCase)
            && requestUri.Port == _apiBaseAddress.Port);

    private static void ApplyBearer(HttpRequestMessage request, string? accessToken)
    {
        request.Headers.Authorization = string.IsNullOrEmpty(accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            byte[] body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var content = new ByteArrayContent(body);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }
}
