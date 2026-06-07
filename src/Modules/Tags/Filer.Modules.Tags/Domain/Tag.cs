using Filer.SharedKernel.Domain;

namespace Filer.Modules.Tags.Domain;

/// <summary>
/// A free-form label owned by a user (02-data-model.md): names are unique per
/// owner — unique (OwnerId, Name) — and every access is ownership-checked via
/// <see cref="IOwnedEntity"/> (05-security.md). Unlike folders and documents,
/// deletion is hard: removing a tag removes its DocumentTag associations (#48),
/// so the entity carries no <c>DeletedAt</c>.
/// </summary>
public sealed class Tag : BaseEntity, IOwnedEntity
{
    /// <summary>
    /// Column bound for <see cref="Name"/> — the single source shared by the EF
    /// mapping and the create validator so they cannot drift. Mirrors
    /// <c>Folder.MaxNameLength</c>.
    /// </summary>
    public const int MaxNameLength = 255;

    public Guid OwnerId { get; set; }

    /// <summary>Reserved for the SaaS evolution; always null in V1 (02-data-model.md).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Display name; unique per owner (02-data-model.md).</summary>
    public string Name { get; set; } = string.Empty;
}
