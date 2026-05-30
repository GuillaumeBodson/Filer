namespace Filer.SharedKernel.Domain;

/// <summary>
/// Base for persisted entities. Identifiers are application-generated UUIDs and
/// timestamps are UTC (02-data-model.md).
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
