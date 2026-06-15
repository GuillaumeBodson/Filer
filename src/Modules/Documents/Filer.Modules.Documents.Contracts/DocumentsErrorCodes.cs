namespace Filer.Modules.Documents.Contracts;

/// <summary>
/// Stable machine-readable error codes surfaced by the Documents module in
/// problem-details responses (03-api-specification.md). Other modules and clients
/// key off these codes, never off the human-readable message.
/// </summary>
public static class DocumentsErrorCodes
{
    /// <summary>The multipart request carried no file, or the file was empty.</summary>
    public const string FileRequired = "file_required";

    /// <summary>The original file name is missing or exceeds the accepted length.</summary>
    public const string FileNameInvalid = "file_name_invalid";

    /// <summary>The file exceeds the configured maximum upload size (04) — 413.</summary>
    public const string FileTooLarge = "file_too_large";

    /// <summary>The declared content type is outside the configured allow-list (04/05) — 415.</summary>
    public const string UnsupportedFileType = "unsupported_file_type";

    /// <summary>The file's magic bytes do not match its declared content type (05) — 415.</summary>
    public const string ContentTypeMismatch = "content_type_mismatch";

    /// <summary>An owned, non-deleted document with identical bytes already exists — 409.</summary>
    public const string DuplicateContent = "duplicate_content";

    /// <summary>The upload could not be completed; no document was created.</summary>
    public const string UploadFailed = "upload_failed";

    /// <summary>
    /// No owned, non-deleted document matches the id — deliberately covers
    /// cross-owner access too (404, never 403; 05-security.md).
    /// </summary>
    public const string DocumentNotFound = "document_not_found";

    /// <summary>The list <c>?page=</c> parameter is below 1 — 400.</summary>
    public const string PageInvalid = "page_invalid";

    /// <summary>The list <c>?pageSize=</c> parameter is outside the accepted range — 400.</summary>
    public const string PageSizeInvalid = "page_size_invalid";

    /// <summary>The list <c>?q=</c> search term exceeds the accepted length — 400.</summary>
    public const string SearchTermInvalid = "search_term_invalid";

    /// <summary>The metadata patch carried no updatable field — 400.</summary>
    public const string UpdateEmpty = "update_empty";

    /// <summary>
    /// The move target is no folder the caller owns — deliberately covers
    /// cross-owner folders too (404, never 403; 05-security.md).
    /// </summary>
    public const string FolderNotFound = "folder_not_found";

    /// <summary>
    /// At least one referenced tag is no tag the caller owns — deliberately covers
    /// cross-owner and missing tags (404, never 403; 05-security.md, ADR-009).
    /// </summary>
    public const string TagNotFound = "tag_not_found";

    /// <summary>
    /// The replace body's <c>tagIds</c> was missing or contained a malformed value
    /// (a non-empty list may legitimately be empty to clear the User tags) — 400.
    /// </summary>
    public const string TagIdsInvalid = "tag_ids_invalid";

    /// <summary>
    /// The apply body's <c>tags</c> was missing or contained a blank name (an empty
    /// list is legitimate — the user may accept none of the suggestions) — 400.
    /// </summary>
    public const string AnalysisTagsInvalid = "analysis_tags_invalid";

    /// <summary>
    /// Nothing to apply: the document has no successfully completed analysis with
    /// a readable result (06-ai-analysis-pipeline.md, Applying Suggestions) — 404.
    /// </summary>
    public const string AnalysisNotFound = "analysis_not_found";

    /// <summary>
    /// A confirmed tag name is not among the stored analysis suggestions — only
    /// suggested tags can be applied through this endpoint — 400.
    /// </summary>
    public const string TagNotSuggested = "tag_not_suggested";

    /// <summary>
    /// A confirmed tag suggestion has no matching tag owned by the caller yet;
    /// create the tag first, then re-apply (no cross-module tag creation) — 400.
    /// </summary>
    public const string SuggestedTagNotCreated = "suggested_tag_not_created";

    /// <summary><c>applyFolder</c> was true but the analysis suggested no folder — 400.</summary>
    public const string FolderNotSuggested = "folder_not_suggested";

    /// <summary>
    /// The folder suggestion proposes a NEW folder; applying it would require
    /// cross-module folder creation, which V1 does not support — create the folder
    /// and move the document explicitly instead — 400.
    /// </summary>
    public const string ProposedFolderNotSupported = "proposed_folder_not_supported";
}
