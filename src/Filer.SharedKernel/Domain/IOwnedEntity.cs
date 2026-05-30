namespace Filer.SharedKernel.Domain;

/// <summary>
/// Marks an entity that belongs to a user. Ownership is validated on every
/// access (05-security.md). <see cref="TenantId"/> is reserved for the future
/// multi-tenant SaaS evolution and is nullable in V1 (02-data-model.md).
/// </summary>
public interface IOwnedEntity
{
    Guid OwnerId { get; }

    Guid? TenantId { get; }
}
