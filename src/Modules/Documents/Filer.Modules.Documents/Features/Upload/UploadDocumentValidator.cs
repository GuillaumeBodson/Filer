using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Documents.Features.Upload;

/// <summary>
/// Structural validation of an upload before any byte is hashed or stored —
/// explicit, dependency-free checks in the slice (13-code-quality-and-design.md).
/// Limits and the allow-list come from configuration (04-non-functional.md).
/// Content sniffing needs the stream and stays in the service.
/// </summary>
internal static class UploadDocumentValidator
{
    public static Result Validate(UploadDocumentCommand command, DocumentsOptions options)
    {
        if (command.SizeBytes <= 0)
        {
            return Result.Failure(Error.Validation(
                "A non-empty file is required.",
                DocumentsErrorCodes.FileRequired));
        }

        if (string.IsNullOrWhiteSpace(command.FileName) || command.FileName.Length > Document.MaxFileNameLength)
        {
            return Result.Failure(Error.Validation(
                $"A file name is required and must not exceed {Document.MaxFileNameLength} characters.",
                DocumentsErrorCodes.FileNameInvalid));
        }

        if (command.SizeBytes > options.MaxUploadBytes)
        {
            return Result.Failure(Error.PayloadTooLarge(
                $"The file exceeds the maximum allowed size of {options.MaxUploadBytes} bytes.",
                DocumentsErrorCodes.FileTooLarge));
        }

        string mediaType = NormalizeMediaType(command.ContentType);
        if (!options.AllowedContentTypes.Any(allowed =>
                string.Equals(NormalizeMediaType(allowed), mediaType, StringComparison.Ordinal)))
        {
            return Result.Failure(Error.UnsupportedMediaType(
                $"The content type '{mediaType}' is not allowed.",
                DocumentsErrorCodes.UnsupportedFileType));
        }

        return Result.Success();
    }

    /// <summary>
    /// Lowercased media type with any parameters stripped, e.g.
    /// <c>text/plain; charset=utf-8</c> → <c>text/plain</c>.
    /// </summary>
    public static string NormalizeMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        int separator = contentType.IndexOf(';', StringComparison.Ordinal);
        string mediaType = separator >= 0 ? contentType[..separator] : contentType;

        return mediaType.Trim().ToLowerInvariant();
    }
}
