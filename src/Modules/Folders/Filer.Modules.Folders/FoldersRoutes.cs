using Filer.WebKernel;

namespace Filer.Modules.Folders;

/// <summary>
/// Route constants internal to the Folders module, mirroring <c>DocumentsRoutes</c>:
/// <see cref="BasePath"/> is written in more than one place — the endpoint group
/// prefix and the synthesized <c>Location</c> of a created folder — so it lives
/// here to keep the two from drifting. The version atom comes from the shared
/// <see cref="ApiRoutes"/> (ADR-006); the module owns only its <c>/folders</c>
/// segment. Internal on purpose: tests restate routes independently so a contract
/// change surfaces as a failing test rather than recompiling silently.
/// </summary>
internal static class FoldersRoutes
{
    /// <summary>Versioned base path for every Folders endpoint (03-api-specification.md).</summary>
    public const string BasePath = ApiRoutes.V1 + "/folders";
}
