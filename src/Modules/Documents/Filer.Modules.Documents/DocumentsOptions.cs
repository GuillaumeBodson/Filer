using System.ComponentModel.DataAnnotations;

namespace Filer.Modules.Documents;

/// <summary>
/// Upload limits and the file-type allow-list. Both are configuration, not code,
/// so they can change without a release (04-non-functional.md). The defaults
/// mirror the V1 values in `04`; a deployment overrides them via the
/// <c>Documents</c> section.
/// </summary>
public sealed class DocumentsOptions
{
    public const string SectionName = "Documents";

    /// <summary>50 MB — the V1 single-file ceiling (04-non-functional.md).</summary>
    public const long DefaultMaxUploadBytes = 50L * 1024 * 1024;

    /// <summary>Maximum accepted file size in bytes; larger uploads get 413.</summary>
    [Range(1, long.MaxValue)]
    public long MaxUploadBytes { get; set; } = DefaultMaxUploadBytes;

    /// <summary>
    /// Declared media types accepted for upload (04-non-functional.md, V1 list).
    /// Compared case-insensitively against the request's content type with any
    /// parameters (e.g. <c>; charset=</c>) stripped.
    /// </summary>
    [MinLength(1)]
    public string[] AllowedContentTypes { get; set; } =
    [
        KnownMediaTypes.Pdf,
        KnownMediaTypes.Png,
        KnownMediaTypes.Jpeg,
        KnownMediaTypes.Webp,
        KnownMediaTypes.Docx,
        KnownMediaTypes.Xlsx,
        KnownMediaTypes.Pptx,
        KnownMediaTypes.PlainText,
        KnownMediaTypes.Markdown,
    ];
}
