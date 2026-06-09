namespace Filer.Modules.Documents.Features.ReplaceTags;

/// <summary>
/// The PUT body: the complete set of <c>User</c>-sourced tags the document should
/// carry (03-api-specification.md). An empty list is legitimate — it clears the
/// document's user tags (AI suggestions are preserved; ADR-009). A null
/// <see cref="TagIds"/> is malformed (400). Duplicate ids are deduplicated rather
/// than rejected, since the composite key already collapses them to one row.
/// </summary>
public sealed record ReplaceDocumentTagsRequest(IReadOnlyList<Guid>? TagIds);
