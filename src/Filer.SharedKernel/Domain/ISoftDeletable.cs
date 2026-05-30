namespace Filer.SharedKernel.Domain;

/// <summary>
/// Marks an entity that is soft-deleted via a nullable <see cref="DeletedAt"/>
/// timestamp rather than physically removed (02-data-model.md).
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}
