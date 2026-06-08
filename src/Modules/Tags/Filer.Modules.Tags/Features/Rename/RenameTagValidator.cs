using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Tags.Features.Rename;

/// <summary>
/// Structural validation of the rename request — explicit, dependency-free checks
/// in the slice (13-code-quality-and-design.md). The name ceiling is
/// <see cref="Tag.MaxNameLength"/>, shared with the EF mapping and the create
/// slice. Validates the trimmed name, since that is what the service persists and
/// compares. The rule is identical to the create slice's: a tag cannot exist
/// without a name.
/// </summary>
internal static class RenameTagValidator
{
    public static Result Validate(RenameTagRequest request)
    {
        string? name = request.Name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length > Tag.MaxNameLength)
        {
            return Result.Failure(Error.Validation(
                $"A tag name is required and must not exceed {Tag.MaxNameLength} characters.",
                TagsErrorCodes.NameInvalid));
        }

        return Result.Success();
    }
}
