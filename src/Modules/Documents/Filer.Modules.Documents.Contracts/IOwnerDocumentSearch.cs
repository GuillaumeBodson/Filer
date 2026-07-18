using Filer.SharedKernel.Paging;

namespace Filer.Modules.Documents.Contracts;

/// <summary>
/// Cross-module read for the Search module (#57, 03-api-specification.md): ranked
/// full-text search over the caller's documents. A narrow contract owned by the
/// module that owns the rows — the tsvector column, its index, and the ranking
/// SQL are Documents persistence concerns, while the Search module owns the HTTP
/// contract, mirroring <c>IFolderContentLookup</c> (10-solution-structure.md).
/// Owner-scoped by construction: a query cannot be built without the caller's id,
/// so results can never contain another owner's documents, and soft-deleted rows
/// are excluded (05-security.md, 02-data-model.md).
/// </summary>
public interface IOwnerDocumentSearch
{
    /// <summary>
    /// One page of the owner's non-deleted documents matching the raw search
    /// term, most relevant first. Term normalization (tokenization, prefix
    /// matching) is this implementation's concern; a term that normalizes to no
    /// usable query yields an empty page, not an error.
    /// </summary>
    Task<PagedResult<DocumentSearchHit>> SearchAsync(
        DocumentSearchQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// A ranked-search request. <see cref="OwnerId"/> comes from the authenticated
/// caller, never from client input (05-security.md); <see cref="Term"/> is the
/// trimmed, validated user term, otherwise raw.
/// </summary>
public sealed record DocumentSearchQuery(
    Guid OwnerId,
    string Term,
    int Page,
    int PageSize);

/// <summary>
/// One search match. <see cref="Score"/> is an opaque relevance value: higher is
/// more relevant, comparable only within a single result page — deliberately not
/// documented as <c>ts_rank</c> so a future semantic sibling (RM-04, pgvector)
/// can fill the same field without breaking the contract (03-api-specification.md).
/// </summary>
public sealed record DocumentSearchHit(
    Guid DocumentId,
    Guid? FolderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    double Score);
