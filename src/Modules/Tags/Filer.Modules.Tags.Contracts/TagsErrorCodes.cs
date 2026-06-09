namespace Filer.Modules.Tags.Contracts;

/// <summary>
/// Stable machine-readable error codes surfaced by the Tags module in
/// problem-details responses (03-api-specification.md). Other modules and clients
/// key off these codes, never off the human-readable message.
/// </summary>
public static class TagsErrorCodes
{
    /// <summary>The tag name is missing, blank, or exceeds the accepted length — 400.</summary>
    public const string NameInvalid = "tag_name_invalid";

    /// <summary>
    /// The caller already has a tag with the same name — unique (OwnerId, Name)
    /// per 02-data-model.md — 409.
    /// </summary>
    public const string NameConflict = "tag_name_conflict";

    /// <summary>
    /// No tag with the given id is owned by the caller — missing or cross-owner,
    /// indistinguishable by the uniform-404 rule (05-security.md) — 404.
    /// </summary>
    public const string TagNotFound = "tag_not_found";
}
