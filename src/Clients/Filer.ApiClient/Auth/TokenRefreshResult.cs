namespace Filer.ApiClient.Auth;

/// <summary>
/// Outcome of a refresh attempt (#167). Only <see cref="Rejected"/> condemns the
/// session — <see cref="BearerTokenHandler"/> clears the stored tokens for that
/// outcome alone, so a server blip during a deploy never signs the user out.
/// </summary>
public enum TokenRefreshResult
{
    /// <summary>A rotated pair is stored (or another caller already rotated it) — retry the request.</summary>
    Refreshed,

    /// <summary>No refresh token, or the server rejected it (401/403) — the session is dead.</summary>
    Rejected,

    /// <summary>
    /// The refresh failed without condemning the refresh token (5xx, malformed
    /// response) — keep the stored tokens; a later request retries the refresh.
    /// </summary>
    TransientFailure,
}
