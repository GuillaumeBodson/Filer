namespace Filer.Modules.Documents.Features.ListDocuments;

/// <summary>
/// One item in the paged envelope of <c>GET /api/v1/documents</c>
/// (03-api-specification.md): an explicit DTO, never the entity — internals such
/// as <c>OwnerId</c> and <c>StorageKey</c> stay server-side (03, no entity
/// leakage; 05-security.md). Field-identical to the get-metadata response, but
/// owned by this slice so the two contracts can diverge independently
/// (vertical slices, 10-solution-structure.md).
/// </summary>
public sealed record DocumentListItemResponse(
    Guid Id,
    Guid? FolderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string ContentHash,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
