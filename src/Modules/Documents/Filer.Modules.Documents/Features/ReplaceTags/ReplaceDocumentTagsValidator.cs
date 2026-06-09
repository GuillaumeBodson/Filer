using Filer.Modules.Documents.Contracts;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Documents.Features.ReplaceTags;

/// <summary>
/// Structural validation of the replace body — explicit, dependency-free checks in
/// the slice (13-code-quality-and-design.md). Only shape is checked here; tag
/// ownership is an authorization concern resolved by the service against
/// <c>ITagOwnershipChecker</c> as a uniform 404 (05-security.md).
/// </summary>
internal static class ReplaceDocumentTagsValidator
{
    public static Result Validate(ReplaceDocumentTagsRequest request)
    {
        // A null array is malformed: PUT must state the set, even if empty. An
        // empty array is valid and means "clear the user tags".
        if (request.TagIds is null)
        {
            return Result.Failure(Error.Validation(
                "The request must provide a 'tagIds' array (which may be empty).",
                DocumentsErrorCodes.TagIdsInvalid));
        }

        // The empty Guid is never a real tag id; reject it rather than 404-ing on a
        // value that can only be a client mistake.
        if (request.TagIds.Any(id => id == Guid.Empty))
        {
            return Result.Failure(Error.Validation(
                "'tagIds' must not contain an empty id.",
                DocumentsErrorCodes.TagIdsInvalid));
        }

        return Result.Success();
    }
}
