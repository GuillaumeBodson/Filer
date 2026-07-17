using Filer.ApiClient.Generated;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;
using Microsoft.Kiota.Abstractions;

namespace Filer.Ui.Folders;

/// <summary>Default <see cref="IFoldersService"/> over the generated client.</summary>
public sealed class FoldersService(FilerApiClient api) : IFoldersService
{
    private readonly FilerApiClient _api = api;

    public async Task<FoldersListResult> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            List<FolderListItemResponse>? folders = await _api.Api.V1.Folders.GetAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return new FoldersListResult(folders ?? [], null);
        }
        catch (ApiException ex)
        {
            return new FoldersListResult(null, ex.ToProblemView());
        }
    }
}
