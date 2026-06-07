namespace Filer.Modules.Documents.Persistence;

/// <summary>
/// Normalized, owner-scoped criteria for the list slice (03-api-specification.md,
/// List filters). Built by the feature service after validation, so the store can
/// trust every value: <paramref name="Page"/> and <paramref name="PageSize"/> are
/// in range and <paramref name="SearchTerm"/> is trimmed, non-empty or null.
/// </summary>
/// <param name="OwnerId">Scope of every list query — never optional (05-security.md).</param>
/// <param name="FolderId">Restrict to one folder when set; null means no folder filter, not "root".</param>
/// <param name="TagId">
/// Restrict to documents carrying the tag. Tags land in M4 (#41–#45); until the
/// DocumentTag join exists no document has any tag, so a set value matches
/// nothing by definition. The parameter is part of the contract from day one
/// (03) so M4 only swaps the store's query.
/// </param>
/// <param name="SearchTerm">Case-insensitive match on the file name; full-text (tsvector) arrives in M6 (#56).</param>
public sealed record DocumentListFilter(
    Guid OwnerId,
    Guid? FolderId,
    Guid? TagId,
    string? SearchTerm,
    int Page,
    int PageSize);
