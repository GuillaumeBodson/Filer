using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Tags.Features.Create;

/// <summary>
/// Structural validation of the create request — explicit, dependency-free checks
/// in the slice (13-code-quality-and-design.md). The name ceiling is
/// <see cref="Tag.MaxNameLength"/>, shared with the EF mapping. Validates the
/// trimmed name, since that is what the service persists and compares.
/// </summary>
internal static class CreateTagValidator
{
    public static Result Validate(CreateTagRequest request)
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
