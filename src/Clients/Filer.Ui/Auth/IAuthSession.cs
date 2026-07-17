using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;

namespace Filer.Ui.Auth;

/// <summary>
/// The auth flows the UI drives (#133): every call goes through the typed Kiota
/// client (ADR-011) and the token store; pages never touch either directly.
/// Operations return <c>null</c> on success or the problem to render (03-api-specification.md).
/// </summary>
public interface IAuthSession
{
    /// <summary>Signs in and stores the issued token pair. Auth state flips via the store's Changed event.</summary>
    Task<ProblemDetailsView?> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Creates the account, then signs in with the same credentials.</summary>
    Task<ProblemDetailsView?> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Revokes the refresh token server-side (best effort) and clears the stored pair.</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads the current user's profile (<c>/auth/me</c>).</summary>
    Task<ProfileResult> GetProfileAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IAuthSession.GetProfileAsync"/>: exactly one side is set.</summary>
public sealed record ProfileResult(MeResponse? Profile, ProblemDetailsView? Problem);
