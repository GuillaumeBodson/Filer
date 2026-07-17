using Filer.ApiClient.Generated.Models;
using Filer.Ui.Folders;
using Filer.Ui.Models;

namespace Filer.Ui.Tests.Documents;

/// <summary>Scriptable <see cref="IFoldersService"/> for page tests.</summary>
internal sealed class FakeFoldersService : IFoldersService
{
    public IReadOnlyList<FolderListItemResponse> Folders { get; set; } = [];
    public ProblemDetailsView? Problem { get; set; }

    public Task<FoldersListResult> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Problem is null
            ? new FoldersListResult(Folders, null)
            : new FoldersListResult(null, Problem));

    public FolderCreateResult? NextCreateResult { get; set; }
    public List<(string Name, Guid? ParentId)> Creates { get; } = [];

    public FolderUpdateResult? NextUpdateResult { get; set; }
    public List<(Guid Id, FolderUpdate Update)> Updates { get; } = [];

    public ProblemDetailsView? NextDeleteResult { get; set; }
    public List<(Guid Id, bool Recursive)> Deletes { get; } = [];

    public Task<FolderCreateResult> CreateAsync(
        string name, Guid? parentId, CancellationToken cancellationToken = default)
    {
        Creates.Add((name, parentId));
        return Task.FromResult(NextCreateResult
            ?? throw new InvalidOperationException("No scripted create result."));
    }

    public Task<FolderUpdateResult> UpdateAsync(
        Guid folderId, FolderUpdate update, CancellationToken cancellationToken = default)
    {
        Updates.Add((folderId, update));
        return Task.FromResult(NextUpdateResult
            ?? throw new InvalidOperationException("No scripted update result."));
    }

    public Task<ProblemDetailsView?> DeleteAsync(
        Guid folderId, bool recursive, CancellationToken cancellationToken = default)
    {
        Deletes.Add((folderId, recursive));
        return Task.FromResult(NextDeleteResult);
    }
}
