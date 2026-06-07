using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Folders.Features.Update;

/// <summary>
/// Structural validation of the rename/move patch — explicit, dependency-free
/// checks in the slice (13-code-quality-and-design.md). The name ceiling is
/// <see cref="Folder.MaxNameLength"/>, shared with the EF mapping and the create
/// slice. Validates the trimmed name, since that is what the service persists
/// and compares.
/// </summary>
internal static class UpdateFolderValidator
{
    public static Result Validate(UpdateFolderRequest request)
    {
        // A patch that touches nothing is a client error, not a silent no-op:
        // surface it instead of stamping UpdatedAt for free.
        if (!request.HasName && !request.HasParentId)
        {
            return Result.Failure(Error.Validation(
                "The request must provide at least one of 'name' or 'parentId'.",
                FoldersErrorCodes.UpdateEmpty));
        }

        // Present-but-null and present-but-blank are both invalid: unlike the
        // parent, a folder cannot exist without a name.
        if (request.HasName)
        {
            string? name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > Folder.MaxNameLength)
            {
                return Result.Failure(Error.Validation(
                    $"A folder name is required and must not exceed {Folder.MaxNameLength} characters.",
                    FoldersErrorCodes.NameInvalid));
            }
        }

        return Result.Success();
    }
}
