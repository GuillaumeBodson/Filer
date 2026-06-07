namespace Filer.Modules.Documents.Features.UpdateMetadata;

/// <summary>
/// The updated document's metadata, restated by this slice rather than shared
/// with GetMetadata so the two contracts can evolve independently
/// (13-code-quality-and-design.md; same stance as the list slice's item DTO).
/// </summary>
public sealed record UpdateDocumentMetadataResponse(
    Guid Id,
    Guid? FolderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string ContentHash,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
