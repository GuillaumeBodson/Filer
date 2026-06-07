using Filer.Modules.Folders.Domain;

namespace Filer.Modules.Folders.Features.Get;

/// <summary>
/// The folder returned by <c>GET /api/v1/folders/{id}</c>
/// (03-api-specification.md): an explicit DTO, never the entity — internals such
/// as <c>OwnerId</c> stay server-side (05-security.md). Field-identical to the
/// create and list responses, but owned by this slice so the contracts can
/// evolve independently (13-code-quality-and-design.md).
/// </summary>
public sealed record GetFolderResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// The slice's single entity → DTO projection (13-code-quality-and-design.md:
    /// explicit projection mapping, owned by the slice).
    /// </summary>
    public static GetFolderResponse From(Folder folder) => new(
        folder.Id,
        folder.ParentId,
        folder.Name,
        folder.CreatedAt,
        folder.UpdatedAt);
}
