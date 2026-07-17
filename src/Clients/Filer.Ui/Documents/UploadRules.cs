using Filer.Ui.Formatting;
using Filer.Ui.Models;

namespace Filer.Ui.Documents;

/// <summary>
/// Client-side pre-checks mirroring the server's upload limits (DocumentsOptions,
/// 04-non-functional.md / 05-security.md). The server stays the authority - these
/// only fail the obvious cases before bytes leave the browser. Codes match the
/// server's so the UI treats both sources uniformly (#169).
/// </summary>
public static class UploadRules
{
    /// <summary>Mirrors <c>DocumentsOptions.DefaultMaxUploadBytes</c> (50 MB, V1 ceiling).</summary>
    public const long MaxSizeBytes = 50L * 1024 * 1024;

    /// <summary>Mirrors <c>DocumentsOptions.AllowedContentTypes</c> (V1 allow-list).</summary>
    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/webp",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain",
        "text/markdown",
    };

    // Browsers leave ContentType blank for extensions they don't know (.md notably).
    private static readonly Dictionary<string, string> ExtensionFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
    };

    /// <summary>
    /// The content type to declare for a file: the browser's when present, otherwise
    /// inferred from the extension. <c>null</c> when neither yields one.
    /// </summary>
    public static string? ResolveContentType(string fileName, string? browserContentType)
    {
        if (!string.IsNullOrWhiteSpace(browserContentType))
        {
            return browserContentType;
        }

        return ExtensionFallbacks.TryGetValue(Path.GetExtension(fileName), out string? inferred)
            ? inferred
            : null;
    }

    /// <summary>The problem to show without calling the server, or <c>null</c> when the file may be sent.</summary>
    public static ProblemDetailsView? Validate(string fileName, string? contentType, long sizeBytes, long maxSizeBytes = MaxSizeBytes)
    {
        if (sizeBytes > maxSizeBytes)
        {
            return new ProblemDetailsView
            {
                Title = "File too large",
                Detail = $"“{fileName}” is {ByteSize.Format(sizeBytes)}; the limit is {ByteSize.Format(maxSizeBytes)}.",
                Code = "file_too_large",
            };
        }

        if (contentType is null || !AllowedContentTypes.Contains(contentType))
        {
            return new ProblemDetailsView
            {
                Title = "Unsupported file type",
                Detail = $"“{fileName}” is not a supported document type. Accepted: PDF, Office (docx/xlsx/pptx), images (png/jpg/webp), text and Markdown.",
                Code = "unsupported_file_type",
            };
        }

        return null;
    }
}
