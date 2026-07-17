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
/// </summary>
public sealed class BearerTokenHandler(ITokenStore tokenStore, ITokenRefresher tokenRefresher)
    : DelegatingHandler
{
    private readonly ITokenStore _tokenStore = tokenStore;
    private readonly ITokenRefresher _tokenRefresher = tokenRefresher;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
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
