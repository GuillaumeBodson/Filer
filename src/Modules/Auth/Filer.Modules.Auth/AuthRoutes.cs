using Filer.WebKernel;

namespace Filer.Modules.Auth;

/// <summary>
/// Route constants internal to the Auth module. <see cref="BasePath"/> is the one
/// segment written in more than one place — the endpoint group prefix
/// (<c>AuthModule.MapAuthEndpoints</c>) and the synthesized <c>Location</c> of a
/// created account (<c>RegisterEndpoint</c>) — so it lives here to keep the two
/// from drifting. The version atom comes from the shared <see cref="ApiRoutes"/>
/// (ADR-006); the module owns only its <c>/auth</c> segment. Per-feature suffixes
/// (<c>/register</c>, <c>/login</c>, <c>/me</c>) appear once each and stay
/// co-located with their slice (ADR-003: duplication beats premature sharing).
///
/// Internal on purpose: tests restate routes independently so a contract change
/// surfaces as a failing test rather than recompiling silently.
/// </summary>
internal static class AuthRoutes
{
    /// <summary>Versioned base path for every Auth endpoint (03-api-specification.md).</summary>
    public const string BasePath = ApiRoutes.V1 + "/auth";
}
