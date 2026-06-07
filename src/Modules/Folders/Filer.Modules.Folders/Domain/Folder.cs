using Filer.SharedKernel.Domain;

namespace Filer.Modules.Folders.Domain;

/// <summary>
/// Hierarchical organization: a folder belongs to one owner and may have a parent
/// (02-data-model.md). Every access is ownership-checked via
/// <see cref="IOwnedEntity"/> (05-security.md); deletion is soft (02). Sibling
/// names are unique per owner — unique (OwnerId, ParentId, Name) — and cycles are
/// prevented in application logic by the rename/move slice (#43).
/// </summary>
public sealed class Folder : BaseEntity, IOwnedEntity, ISoftDeletable
{
    /// <summary>
    /// Column bound for <see cref="Name"/> — the single source shared by the EF
    /// mapping and the create validator so they cannot drift. Mirrors
    /// <c>Document.MaxFileNameLength</c>.
    /// </summary>
    public const int MaxNameLength = 255;

    public Guid OwnerId { get; set; }

    /// <summary>Reserved for the SaaS evolution; always null in V1 (02-data-model.md).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Null = top level (02-data-model.md).</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Display name; unique among active siblings per owner (02-data-model.md).</summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset? DeletedAt { get; set; }
}
