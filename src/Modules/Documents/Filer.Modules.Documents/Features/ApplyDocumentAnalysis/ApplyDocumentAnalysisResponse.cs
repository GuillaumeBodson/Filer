using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.ReplaceTags;

namespace Filer.Modules.Documents.Features.ApplyDocumentAnalysis;

/// <summary>
/// The outcome of applying confirmed suggestions (#55): what was applied and the
/// document's resulting organisation. Reuses <see cref="DocumentTagItem"/> so a
/// client reads association sources identically across the tag endpoints — the
/// applied rows show up as <c>AiSuggested</c> (02-data-model.md).
/// </summary>
public sealed record ApplyDocumentAnalysisResponse(
    Guid DocumentId,
    bool FolderApplied,
    Guid? FolderId,
    IReadOnlyList<DocumentTagItem> Tags)
{
    /// <summary>
    /// The slice's single entity → DTO projection (13-code-quality-and-design.md),
    /// mirroring <c>DocumentTagsResponse.From</c> for the association set.
    /// </summary>
    public static ApplyDocumentAnalysisResponse From(
        Document document, bool folderApplied, IReadOnlyList<DocumentTag> associations) =>
        new(
            document.Id,
            folderApplied,
            document.FolderId,
            associations
                .OrderBy(a => a.TagId)
                .Select(a => new DocumentTagItem(a.TagId, a.Source.ToString()))
                .ToList());
}
