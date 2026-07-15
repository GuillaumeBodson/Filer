namespace Filer.Modules.AiAnalysis.Contracts;

/// <summary>
/// Provider-neutral input for one analysis run (06-ai-analysis-pipeline.md,
/// Provider Abstraction). Carries the document's extracted text — never a storage
/// path or vendor handle — plus the owner's existing folders and tags so providers
/// can prefer the user's own organisation over inventing new names.
/// </summary>
/// <param name="DocumentId">The document under analysis.</param>
/// <param name="FileName">Original file name; a strong signal for folder/tag suggestions.</param>
/// <param name="ContentType">Declared MIME type of the document.</param>
/// <param name="Text">
/// The document's extracted text. Extraction is the worker's concern (#53); providers
/// receive ready-to-prompt text and never read blobs themselves (07).
/// </param>
/// <param name="ExistingFolders">
/// The owner's folder tree (flattened; <see cref="ExistingFolder.ParentId"/> carries
/// the hierarchy, <see cref="ExistingFolder.DocumentCount"/> where documents live),
/// so providers suggest into the existing organisation instead of inventing parallel
/// folders (#118). A provider that picks one echoes its id back via
/// <see cref="FolderSuggestion.ExistingFolderId"/> so applying needs no name lookup.
/// </param>
/// <param name="ExistingTags">The owner's tag names, matched or extended by suggestions.</param>
public sealed record DocumentAnalysisRequest(
    Guid DocumentId,
    string FileName,
    string ContentType,
    string Text,
    IReadOnlyList<ExistingFolder> ExistingFolders,
    IReadOnlyList<string> ExistingTags);

/// <summary>An owner folder offered to the provider as suggestion context.</summary>
/// <param name="Id">The folder's id, echoed back when a provider picks it.</param>
/// <param name="Name">The folder's display name.</param>
/// <param name="ParentId">
/// The parent folder's id, or null at the top level (02-data-model.md — Folder
/// self-references via <c>ParentId</c>). Lets providers see the tree, not a flat
/// list. Defaulted so the extension is additive (#118); providers that ignore it
/// (e.g. the Fake) are unaffected.
/// </param>
/// <param name="DocumentCount">
/// How many of the owner's non-deleted documents sit directly in this folder — a
/// signal for where the user actually files things. Defaulted, additive (#118).
/// </param>
public sealed record ExistingFolder(Guid Id, string Name, Guid? ParentId = null, int DocumentCount = 0);
