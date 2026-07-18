namespace Filer.Modules.Search.Features.SearchDocuments;

/// <summary>
/// The search request as bound from the query string (03-api-specification.md,
/// Search): every parameter raw and unnormalized — validation and defaulting
/// happen in the slice, not at the binding boundary. Unlike the list's optional
/// filter, <c>q</c> is the point of the endpoint and is required by validation.
/// </summary>
public sealed record SearchDocumentsQuery(
    string? SearchTerm,
    int? Page,
    int? PageSize);
