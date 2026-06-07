namespace Filer.Modules.Documents.Features.ListDocuments;

/// <summary>
/// The list request as bound from the query string (03-api-specification.md,
/// List filters): every parameter optional, raw and unnormalized — validation
/// and defaulting happen in the slice, not at the binding boundary.
/// </summary>
public sealed record ListDocumentsQuery(
    Guid? FolderId,
    Guid? TagId,
    string? SearchTerm,
    int? Page,
    int? PageSize);
