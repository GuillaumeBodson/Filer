using Filer.ApiClient.Auth;
using Filer.ApiClient.Generated;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;
using Microsoft.Kiota.Abstractions;

namespace Filer.Ui.Auth;

/// <summary>
/// Default <see cref="IAuthSession"/> over the generated <see cref="FilerApiClient"/>
/// and the host's <see cref="ITokenStore"/>. Tokens are stored, never logged
/// (05-security.md); declared error responses surface as <see cref="ProblemDetailsView"/>.
/// </summary>
public sealed class AuthSession(FilerApiClient api, ITokenStore tokenStore) : IAuthSession
{
    private readonly FilerApiClient _api = api;
    private readonly ITokenStore _tokenStore = tokenStore;

    public async Task<ProblemDetailsView?> LoginAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            LoginResponse? response = await _api.Api.V1.Auth.Login.PostAsync(
                new LoginRequest { Email = email, Password = password },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response?.AccessToken is null || response.RefreshToken is null)
            {
                return EmptyResponseProblem("Sign-in failed");
            }

            await _tokenStore.SaveAsync(
                new TokenPair(
                    response.AccessToken,
                    response.ExpiresAt,
                    response.RefreshToken,
                    response.RefreshTokenExpiresAt),
                cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (ApiException ex)
        {
            return ex.ToProblemView();
        }
    }

    public async Task<ProblemDetailsView?> RegisterAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            await _api.Api.V1.Auth.Register.PostAsync(
                new RegisterRequest { Email = email, Password = password },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            return ex.ToProblemView();
        }

        // Register issues no tokens (03-api-specification.md) - sign in right after so
        // the user lands authenticated.
        return await LoginAsync(email, password, cancellationToken).ConfigureAwait(false);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        TokenPair? tokens = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!string.IsNullOrEmpty(tokens?.RefreshToken))
            {
                await _api.Api.V1.Auth.Logout.PostAsync(
                    new LogoutRequest { RefreshToken = tokens.RefreshToken },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ApiException)
        {
            // Best effort: revocation failing (expired token, server blip) must not
            // keep the user signed in locally.
        }
        finally
        {
            await _tokenStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ProfileResult> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            MeResponse? me = await _api.Api.V1.Auth.Me.GetAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return me is null
                ? new ProfileResult(null, EmptyResponseProblem("Profile unavailable"))
                : new ProfileResult(me, null);
        }
        catch (ApiException ex)
        {
            return new ProfileResult(null, ex.ToProblemView());
        }
    }

    // Same "server returned an empty body" fallback shape as the other UI services.
    private static ProblemDetailsView EmptyResponseProblem(string title) => new()
    {
        Title = title,
        Detail = "The server returned an empty response. Try again.",
    };
}
