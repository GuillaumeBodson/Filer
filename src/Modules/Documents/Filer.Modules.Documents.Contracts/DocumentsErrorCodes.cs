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
}
