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
}
