namespace Filer.Modules.Folders.Features.Create;

/// <summary>
/// The POST body (03-api-specification.md): a required display name and an
/// optional parent. Unlike the Documents PATCH, presence tracking is unnecessary —
/// creation has no merge-patch semantics — so a plain record suffices.
/// </summary>
public sealed record CreateFolderRequest(string? Name, Guid? ParentId);
