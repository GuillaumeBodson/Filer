using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;

namespace Filer.Ui.Tags;

/// <summary>
/// Tag management for the UI (#139): CRUD over the owner's flat tag list, through
/// the typed Kiota client (ADR-011). Per-owner name uniqueness violations surface
/// as <c>tag_name_conflict</c> problems.
/// </summary>
public interface ITagsService
{
    Task<TagsListResult> ListAsync(CancellationToken cancellationToken = default);

    Task<TagMutationResult> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<TagMutationResult> RenameAsync(Guid tagId, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes the tag (associations detach server-side). Returns <c>null</c> on success.</summary>
    Task<ProblemDetailsView?> DeleteAsync(Guid tagId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a tag list: exactly one side is set.</summary>
public sealed record TagsListResult(
    IReadOnlyList<TagListItemResponse>? Tags,
    ProblemDetailsView? Problem);

/// <summary>Outcome of a create/rename: the tag's id and name, or the problem.</summary>
public sealed record TagMutationResult(
    Guid? TagId,
    string? Name,
    ProblemDetailsView? Problem);
