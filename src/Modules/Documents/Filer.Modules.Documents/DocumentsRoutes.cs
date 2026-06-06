using Filer.WebKernel;

namespace Filer.Modules.Documents;

/// <summary>
/// Route constants internal to the Documents module, mirroring <c>AuthRoutes</c>:
/// <see cref="BasePath"/> is written in more than one place — the endpoint group
/// prefix and the synthesized <c>Location</c> of a created document — so it lives
/// here to keep the two from drifting. The version atom comes from the shared
/// <see cref="ApiRoutes"/> (ADR-006); the module owns only its <c>/documents</c>
/// segment. Internal on purpose: tests restate routes independently so a contract
/// change surfaces as a failing test rather than recompiling silently.
/// </summary>
internal static class DocumentsRoutes
{
    /// <summary>Versioned base path for every Documents endpoint (03-api-specification.md).</summary>
    public const string BasePath = ApiRoutes.V1 + "/documents";
}
