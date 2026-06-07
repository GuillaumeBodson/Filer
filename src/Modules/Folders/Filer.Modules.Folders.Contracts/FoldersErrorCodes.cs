namespace Filer.Modules.Folders.Contracts;

/// <summary>
/// Stable machine-readable error codes surfaced by the Folders module in
/// problem-details responses (03-api-specification.md). Other modules and clients
/// key off these codes, never off the human-readable message.
/// </summary>
public static class FoldersErrorCodes
{
    /// <summary>The folder name is missing, blank, or exceeds the accepted length — 400.</summary>
    public const string NameInvalid = "folder_name_invalid";

    /// <summary>
    /// The requested parent folder does not exist for the caller. Cross-owner and
    /// missing parents are indistinguishable by design (05-security.md) — 404.
    /// </summary>
    public const string ParentNotFound = "parent_folder_not_found";

    /// <summary>
    /// The caller already has an active folder with the same name under the same
    /// parent — unique (OwnerId, ParentId, Name) per 02-data-model.md — 409.
    /// </summary>
    public const string NameConflict = "folder_name_conflict";

    /// <summary>
    /// The list <c>view</c> parameter is neither <c>flat</c> nor <c>tree</c>
    /// (03-api-specification.md: invalid value → 400).
    /// </summary>
    public const string ViewInvalid = "folder_view_invalid";
}
