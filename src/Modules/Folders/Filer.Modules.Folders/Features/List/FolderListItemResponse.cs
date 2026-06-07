using System.Text.Json.Serialization;
using Filer.Modules.Folders.Domain;

namespace Filer.Modules.Folders.Features.List;

/// <summary>
/// One folder in the response of <c>GET /api/v1/folders</c>
/// (03-api-specification.md): an explicit DTO, never the entity — internals such
/// as <c>OwnerId</c> stay server-side (05-security.md). One type serves both
/// views: <see cref="Children"/> is null in the flat view and omitted from the
/// JSON, and always a (possibly empty) list on every tree node, so the two wire
/// shapes differ only by that property. Restated by this slice rather than shared
/// with the create slice so the contracts can evolve independently
/// (13-code-quality-and-design.md).
/// </summary>
public sealed record FolderListItemResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FolderListItemResponse>? Children = null)
{
    /// <summary>
    /// The slice's single entity → DTO projection (13-code-quality-and-design.md:
    /// explicit projection mapping, owned by the slice). A null
    /// <paramref name="children"/> means the flat shape, not an empty tree node.
    /// </summary>
    public static FolderListItemResponse From(
        Folder folder, IReadOnlyList<FolderListItemResponse>? children = null) => new(
        folder.Id,
        folder.ParentId,
        folder.Name,
        folder.CreatedAt,
        folder.UpdatedAt,
        children);
}
