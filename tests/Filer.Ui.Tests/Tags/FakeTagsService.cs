using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;
using Filer.Ui.Tags;

namespace Filer.Ui.Tests.Tags;

/// <summary>Scriptable <see cref="ITagsService"/>; records every call.</summary>
internal sealed class FakeTagsService : ITagsService
{
    public IReadOnlyList<TagListItemResponse> Tags { get; set; } = [];
    public ProblemDetailsView? ListProblem { get; set; }

    public TagMutationResult? NextCreateResult { get; set; }
    public List<string> Creates { get; } = [];

    public TagMutationResult? NextRenameResult { get; set; }
    public List<(Guid Id, string Name)> Renames { get; } = [];

    public ProblemDetailsView? NextDeleteResult { get; set; }
    public List<Guid> Deletes { get; } = [];

    public Task<TagsListResult> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ListProblem is null
            ? new TagsListResult(Tags, null)
            : new TagsListResult(null, ListProblem));

    public Task<TagMutationResult> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        Creates.Add(name);
        return Task.FromResult(NextCreateResult
            ?? throw new InvalidOperationException("No scripted create result."));
    }

    public Task<TagMutationResult> RenameAsync(Guid tagId, string name, CancellationToken cancellationToken = default)
    {
        Renames.Add((tagId, name));
        return Task.FromResult(NextRenameResult
            ?? throw new InvalidOperationException("No scripted rename result."));
    }

    public Task<ProblemDetailsView?> DeleteAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        Deletes.Add(tagId);
        return Task.FromResult(NextDeleteResult);
    }
}
