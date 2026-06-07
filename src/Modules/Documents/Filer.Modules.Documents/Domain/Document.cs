using Filer.SharedKernel.Domain;

namespace Filer.Modules.Documents.Domain;

/// <summary>
/// The central entity: metadata only — the bytes live behind
/// <c>IFileStorageProvider</c> under <see cref="StorageKey"/> (02-data-model.md,
/// 07-storage-and-deployment.md). Every access is ownership-checked via
/// <see cref="IOwnedEntity"/> (05-security.md); deletion is soft (02).
/// </summary>
public sealed class Document : BaseEntity, IOwnedEntity, ISoftDeletable
{
    public Guid OwnerId { get; set; }

    /// <summary>Reserved for the SaaS evolution; always null in V1 (02-data-model.md).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Null = root / unfiled. The Folders slice is a separate feature (#34+).</summary>
    public Guid? FolderId { get; set; }

    /// <summary>
    /// Original client file name, stored as metadata only — it never participates
    /// in storage paths (05-security.md, filename sanitization).
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Opaque key resolved by <c>IFileStorageProvider</c> (07).</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>SHA-256 of the bytes (lowercase hex); drives duplicate detection (02).</summary>
    public string ContentHash { get; set; } = string.Empty;

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    /// <summary>Flexible extra attributes as JSONB; unused at upload time (02).</summary>
    public string? Metadata { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
