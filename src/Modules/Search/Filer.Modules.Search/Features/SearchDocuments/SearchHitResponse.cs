using Filer.Modules.Documents.Contracts;

namespace Filer.Modules.Search.Features.SearchDocuments;

/// <summary>
/// One item in the paged envelope of <c>GET /api/v1/search</c>
/// (03-api-specification.md): an explicit DTO owned by this slice, mirroring the
/// Documents list item so clients render both collections the same way.
/// <see cref="Score"/> is opaque relevance — higher is more relevant, comparable
/// only within a single response, never documented as <c>ts_rank</c> — so a
/// future semantic sibling (RM-04, pgvector) can fill the same field without
/// breaking clients.
/// </summary>
public sealed record SearchHitResponse(
    Guid Id,
    Guid? FolderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    double Score)
{
    /// <summary>
    /// The slice's single contract-hit → DTO projection
    /// (13-code-quality-and-design.md).
    /// </summary>
    public static SearchHitResponse From(DocumentSearchHit hit) => new(
        hit.DocumentId,
        hit.FolderId,
        hit.FileName,
        hit.ContentType,
        hit.SizeBytes,
        hit.Status,
        hit.CreatedAt,
        hit.UpdatedAt,
        hit.Score);
}
