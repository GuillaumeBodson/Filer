using Filer.Modules.Documents.Domain;

namespace Filer.Modules.Documents.Features.ReplaceTags;

/// <summary>
/// The document's tag associations after a mutation (#49). Restated by this slice
/// rather than leaking the entity (03-api-specification.md: typed DTOs, no entity
/// leakage). Each item reports the tag id and its <c>Source</c> so a client can
/// tell a user tag from an AI suggestion. Shared by the replace and add slices —
/// both return the resulting set.
/// </summary>
public sealed record DocumentTagsResponse(Guid DocumentId, IReadOnlyList<DocumentTagItem> Tags)
{
    public static DocumentTagsResponse From(Guid documentId, IReadOnlyList<DocumentTag> associations) =>
        new(
            documentId,
            associations
                .OrderBy(a => a.TagId)
                .Select(a => new DocumentTagItem(a.TagId, a.Source.ToString()))
                .ToList());
}

/// <summary>One tag association: the tag id and who created it (User / AiSuggested).</summary>
public sealed record DocumentTagItem(Guid TagId, string Source);
