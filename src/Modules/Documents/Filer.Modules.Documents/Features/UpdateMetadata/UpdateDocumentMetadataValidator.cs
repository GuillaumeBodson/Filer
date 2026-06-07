using Filer.Modules.Documents.Contracts;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Documents.Features.UpdateMetadata;

/// <summary>
/// Structural validation of the metadata patch — explicit, dependency-free checks
/// in the slice (13-code-quality-and-design.md). The file-name ceiling matches
/// the upload validator and the FileName column bound (255).
/// </summary>
internal static class UpdateDocumentMetadataValidator
{
    /// <summary>Mirrors <c>UploadDocumentValidator.MaxFileNameLength</c> and the column bound.</summary>
    internal const int MaxFileNameLength = 255;

    public static Result Validate(UpdateDocumentMetadataRequest request)
    {
        // A patch that touches nothing is a client error, not a silent no-op:
        // surface it instead of stamping UpdatedAt for free.
        if (!request.HasFileName && !request.HasFolderId)
        {
            return Result.Failure(Error.Validation(
                "The request must provide at least one of 'fileName' or 'folderId'.",
                DocumentsErrorCodes.UpdateEmpty));
        }

        // Present-but-null and present-but-blank are both invalid: unlike the
        // folder, a document cannot exist without a file name.
        if (request.HasFileName
            && (string.IsNullOrWhiteSpace(request.FileName) || request.FileName.Length > MaxFileNameLength))
        {
            return Result.Failure(Error.Validation(
                $"A file name is required and must not exceed {MaxFileNameLength} characters.",
                DocumentsErrorCodes.FileNameInvalid));
        }

        return Result.Success();
    }
}
