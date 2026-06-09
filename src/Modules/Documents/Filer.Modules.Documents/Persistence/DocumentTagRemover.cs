using Filer.Modules.Documents.Contracts;

namespace Filer.Modules.Documents.Persistence;

/// <summary>
/// The module's implementation of the tag-delete cascade contract (ADR-009, #48):
/// a thin adapter over <see cref="IDocumentStore"/>, mirroring
/// <c>FolderDocumentRemover</c> for folders. There is no job queue to touch here —
/// removing a tag association changes no document's content or analysis state — so
/// unlike the folder cascade this is a single owner-scoped delete with nothing to
/// fail after it (13-code-quality-and-design.md, no anticipation).
/// </summary>
internal sealed class DocumentTagRemover(IDocumentStore documents) : IDocumentTagRemover
{
    public Task RemoveAllForTagAsync(
        Guid ownerId, Guid tagId, CancellationToken cancellationToken) =>
        documents.RemoveDocumentTagsForTagAsync(ownerId, tagId, cancellationToken);
}
