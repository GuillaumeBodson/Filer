namespace Filer.WebKernel;

/// <summary>
/// Versioned API route prefixes shared across modules (03-api-specification.md).
/// A module composes its own base path from the version it serves —
/// e.g. <c>ApiRoutes.V1 + "/auth"</c> — so the version atom is centralised while the
/// per-module segment stays owned by the module.
///
/// Versions are additive and independent: when the first module adopts v2, add a
/// <c>V2</c> constant here; modules choose their version per endpoint, with no
/// lockstep across modules (ADR-006).
/// </summary>
public static class ApiRoutes
{
    /// <summary>Version 1 prefix — the only version shipping today.</summary>
    public const string V1 = "/api/v1";
}
