using Filer.Modules.Documents.Contracts;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Documents.Features.ApplyDocumentAnalysis;

/// <summary>
/// Structural validation of the apply body — explicit, dependency-free checks in
/// the slice (13-code-quality-and-design.md). Only shape is checked here; whether
/// a name matches a suggestion or an owned tag is the service's business against
/// the stored result and the Tags contract.
/// </summary>
internal static class ApplyDocumentAnalysisValidator
{
    public static Result Validate(ApplyDocumentAnalysisRequest request)
    {
        // A null array is malformed: the body must state the confirmed set, even
        // if empty — accepting none of the suggestions is a legitimate choice
        // (06-ai-analysis-pipeline.md, Applying Suggestions).
        if (request.Tags is null)
        {
            return Result.Failure(Error.Validation(
                "The request must provide a 'tags' array (which may be empty).",
                DocumentsErrorCodes.AnalysisTagsInvalid));
        }

        // A blank name can never match a suggestion; reject it as a client mistake
        // rather than failing the match later with a misleading message.
        if (request.Tags.Any(string.IsNullOrWhiteSpace))
        {
            return Result.Failure(Error.Validation(
                "'tags' must not contain an empty name.",
                DocumentsErrorCodes.AnalysisTagsInvalid));
        }

        return Result.Success();
    }
}
