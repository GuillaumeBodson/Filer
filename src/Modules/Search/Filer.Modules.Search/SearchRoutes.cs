using Filer.WebKernel;

namespace Filer.Modules.Search;

/// <summary>
/// Route constants internal to the Search module, mirroring
/// <c>DocumentsRoutes</c>. The version atom comes from the shared
/// <see cref="ApiRoutes"/> (ADR-006); the module owns only its <c>/search</c>
/// segment. Internal on purpose: tests restate routes independently so a
/// contract change surfaces as a failing test rather than recompiling silently.
/// </summary>
internal static class SearchRoutes
{
    /// <summary>Versioned base path for the Search endpoint (03-api-specification.md).</summary>
    public const string BasePath = ApiRoutes.V1 + "/search";
}
