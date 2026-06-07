namespace Filer.Modules.Documents.Features.Upload;

/// <summary>
/// Outcome of a structurally valid upload: either a created document or a
/// duplicate hit. A duplicate is a first-class outcome, not an <c>Error</c> —
/// the spec returns the existing document's reference so the client can decide
/// whether to proceed (03-api-specification.md, upload behavior), and the flat
/// <c>Error</c> shape cannot carry that reference.
/// </summary>
public sealed record UploadDocumentResult
{
    private UploadDocumentResult(UploadDocumentResponse? document, Guid? duplicateOfDocumentId)
    {
        Document = document;
        DuplicateOfDocumentId = duplicateOfDocumentId;
    }

    /// <summary>The created document; null when <see cref="IsDuplicate"/>.</summary>
    public UploadDocumentResponse? Document { get; }

    /// <summary>The existing owned document with identical bytes; null when created.</summary>
    public Guid? DuplicateOfDocumentId { get; }

    public bool IsDuplicate => DuplicateOfDocumentId is not null;

    public static UploadDocumentResult Created(UploadDocumentResponse document) =>
        new(document, null);

    public static UploadDocumentResult Duplicate(Guid existingDocumentId) =>
        new(null, existingDocumentId);
}
