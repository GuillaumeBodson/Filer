namespace Filer.Modules.Documents.Contracts;

/// <summary>
/// The cross-module seam the Tags module deletes document-tag associations through
/// (ADR-009, #48): when a tag is hard-deleted, every <c>DocumentTag</c> row that
/// references it must go with it — the join lives in the Documents schema, so the
/// Documents module owns its removal. A narrow contract owned by the module that
/// owns the data, mirroring <c>IFolderDocumentRemover</c> and
/// <c>ITagOwnershipChecker</c> in the other directions (10-solution-structure.md).
/// </summary>
public interface IDocumentTagRemover
{
    /// <summary>
    /// Removes every <c>DocumentTag</c> association for <paramref name="tagId"/>
    /// whose document belongs to <paramref name="ownerId"/>. Owner-scoped like
    /// every cross-module write (05-security.md): the join carries no owner of its
    /// own, so ownership is enforced transitively through the document — a foreign
    /// owner's id can never touch the caller's rows, and a cross-owner tag id
    /// simply matches none of the caller's documents. Idempotent: a tag with no
    /// associations (or already removed) is a success that removes nothing, so the
    /// tag-delete cascade is safe to retry.
    /// </summary>
    Task RemoveAllForTagAsync(Guid ownerId, Guid tagId, CancellationToken cancellationToken);
}
