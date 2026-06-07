using Filer.Modules.Folders.Domain;

namespace Filer.Modules.Folders.Features.Create;

/// <summary>
/// The created folder, restated by this slice rather than shared with the future
/// get/list slices so the contracts can evolve independently
/// (13-code-quality-and-design.md; same stance as the Documents slices).
/// </summary>
public sealed record CreateFolderResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CreateFolderResponse From(Folder folder) => new(
        folder.Id,
        folder.ParentId,
        folder.Name,
        folder.CreatedAt,
        folder.UpdatedAt);
}
