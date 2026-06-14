using System.Security.Claims;
using Filer.ApiClient.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace Filer.Ui.Auth;

/// <summary>
/// Derives Blazor's authentication state from the stored tokens (05-security.md). It
/// subscribes to <see cref="ITokenStore.Changed"/>, so sign-in, sign-out and a failed
/// refresh (which clears the store) all flow to <c>AuthorizeView</c>/<c>AuthorizeRouteView</c>
/// without callers having to notify it explicitly.
/// </summary>
public sealed class FilerAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly ITokenStore _tokenStore;

    public FilerAuthenticationStateProvider(ITokenStore tokenStore)
    {
        _tokenStore = tokenStore;
        _tokenStore.Changed += OnTokensChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        TokenPair? tokens = await _tokenStore.GetAsync().ConfigureAwait(false);
        if (tokens?.AccessToken is null)
        {
            return Anonymous;
        }

        ClaimsIdentity? identity = JwtClaims.ToIdentity(tokens.AccessToken);
        return identity is null ? Anonymous : new AuthenticationState(new ClaimsPrincipal(identity));
    }

    private void OnTokensChanged(object? sender, EventArgs e) =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => _tokenStore.Changed -= OnTokensChanged;
}
