namespace Filer.Modules.Folders.Features.List;

/// <summary>
/// The two listing shapes of <c>GET /api/v1/folders</c>
/// (03-api-specification.md: <c>?view=tree|flat</c>, default <c>flat</c>).
/// Internal: the wire value is the string parsed by
/// <see cref="ListFoldersValidator"/>, never this enum's names.
/// </summary>
internal enum FolderListView
{
    Flat,
    Tree,
}
