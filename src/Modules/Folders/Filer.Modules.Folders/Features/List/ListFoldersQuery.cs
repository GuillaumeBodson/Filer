namespace Filer.Modules.Folders.Features.List;

/// <summary>
/// The list request as bound from the query string (03-api-specification.md):
/// the optional <c>view</c> parameter, raw and unnormalized — parsing and
/// defaulting happen in the slice, not at the binding boundary (same stance as
/// <c>ListDocumentsQuery</c>).
/// </summary>
public sealed record ListFoldersQuery(string? View);
