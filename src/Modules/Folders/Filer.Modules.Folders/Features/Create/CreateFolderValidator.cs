using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Folders.Features.Create;

/// <summary>
/// Structural validation of the create request — explicit, dependency-free checks
/// in the slice (13-code-quality-and-design.md). The name ceiling is
/// <see cref="Folder.MaxNameLength"/>, shared with the EF mapping. Validates the
/// trimmed name, since that is what the service persists and compares.
/// </summary>
internal static class CreateFolderValidator
{
    public static Result Validate(CreateFolderRequest request)
    {
        string? name = request.Name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length > Folder.MaxNameLength)
        {
            return Result.Failure(Error.Validation(
                $"A folder name is required and must not exceed {Folder.MaxNameLength} characters.",
                FoldersErrorCodes.NameInvalid));
        }

        return Result.Success();
    }
}
