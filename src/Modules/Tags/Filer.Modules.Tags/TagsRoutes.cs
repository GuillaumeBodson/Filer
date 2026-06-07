using Filer.WebKernel;

namespace Filer.Modules.Tags;

/// <summary>
/// Route constants internal to the Tags module, mirroring <c>FoldersRoutes</c>:
/// <see cref="BasePath"/> is written in more than one place — the endpoint group
/// prefix and the synthesized <c>Location</c> of a created tag — so it lives here
/// to keep the two from drifting. The version atom comes from the shared
/// <see cref="ApiRoutes"/> (ADR-006); the module owns only its <c>/tags</c>
/// segment. Internal on purpose: tests restate routes independently so a contract
/// change surfaces as a failing test rather than recompiling silently.
/// </summary>
internal static class TagsRoutes
{
    /// <summary>Versioned base path for every Tags endpoint (03-api-specification.md).</summary>
    public const string BasePath = ApiRoutes.V1 + "/tags";
}
