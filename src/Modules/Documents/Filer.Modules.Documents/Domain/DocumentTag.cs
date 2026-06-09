namespace Filer.Modules.Documents.Domain;

/// <summary>
/// The many-to-many join between a document and a tag (02-data-model.md). The
/// Documents module owns this table in its own schema (ADR-009): the join lives
/// with the document, not the tag. The composite key <c>(DocumentId, TagId)</c>
/// means exactly one row per pair, so re-adding a tag promotes the existing row's
/// <see cref="Source"/> rather than inserting a duplicate. <see cref="TagId"/> is
/// a plain Guid — the Tag lives in another module's schema, so there is no EF
/// navigation or cross-schema FK; tag ownership is validated in the app layer via
/// <c>ITagOwnershipChecker</c> (ADR-009). Not a <c>BaseEntity</c>: the pair is the
/// identity, and the join carries only <see cref="CreatedAt"/>.
/// </summary>
public sealed class DocumentTag
{
    /// <summary>The owning document (FK within this context; cascades on document delete).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>The associated tag — a plain column, no FK across schemas (ADR-009).</summary>
    public Guid TagId { get; set; }

    /// <summary>Who created the association; governs the replace/promote semantics (ADR-009).</summary>
    public DocumentTagSource Source { get; set; } = DocumentTagSource.User;

    public DateTimeOffset CreatedAt { get; set; }
}
