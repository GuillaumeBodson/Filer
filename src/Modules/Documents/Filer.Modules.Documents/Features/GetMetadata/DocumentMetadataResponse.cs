using Filer.Modules.Documents.Domain;

namespace Filer.Modules.Documents.Features.GetMetadata;

/// <summary>
/// The document metadata contract for <c>GET /api/v1/documents/{id}</c>
/// (03-api-specification.md): an explicit DTO, never the entity — internals such
/// as <c>OwnerId</c> and <c>StorageKey</c> stay server-side (03, no entity
/// leakage; 05-security.md). Mirrors the upload response's field shape
/// (<c>Status</c> as string) and adds what only a read returns: <c>FolderId</c>
/// and <c>UpdatedAt</c>.
/// </summary>
public sealed record DocumentMetadataResponse(
    Guid Id,
    Guid? FolderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string ContentHash,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// The slice's single entity → DTO projection (13-code-quality-and-design.md:
    /// explicit constructor/projection mapping, owned by the slice).
    /// </summary>
    public static DocumentMetadataResponse From(Document document) => new(
        document.Id,
        document.FolderId,
        document.FileName,
        document.ContentType,
        document.SizeBytes,
        document.ContentHash,
        document.Status.ToString(),
        document.CreatedAt,
        document.UpdatedAt);
}
