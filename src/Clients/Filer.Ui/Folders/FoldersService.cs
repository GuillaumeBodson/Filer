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

    public async Task<FolderCreateResult> CreateAsync(
        string name, Guid? parentId, CancellationToken cancellationToken = default)
    {
        try
        {
            CreateFolderResponse? created = await _api.Api.V1.Folders.PostAsync(
                new CreateFolderRequest { Name = name, ParentId = parentId },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return created is null
                ? new FolderCreateResult(null, EmptyResponseProblem("Folder not created"))
                : new FolderCreateResult(created, null);
        }
        catch (ApiException ex)
        {
            return new FolderCreateResult(null, ex.ToProblemView());
        }
    }

    public async Task<FolderUpdateResult> UpdateAsync(
        Guid folderId, FolderUpdate update, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new UpdateFolderRequest();
            if (update.NewName is not null)
            {
                body.Name = update.NewName;
            }

            if (update.MoveParent)
            {
                if (update.TargetParentId is Guid target)
                {
                    body.ParentId = target;
                }
                else
                {
                    // Merge-patch: an explicit "parentId": null re-parents to the root;
                    // the generated writer skips null typed properties, AdditionalData not.
                    body.AdditionalData["parentId"] = null!;
                }
            }

            UpdateFolderResponse? updated = await _api.Api.V1.Folders[folderId]
                .PatchAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

            return updated is null
                ? new FolderUpdateResult(null, EmptyResponseProblem("Folder not updated"))
                : new FolderUpdateResult(updated, null);
        }
        catch (ApiException ex)
        {
            return new FolderUpdateResult(null, ex.ToProblemView());
        }
    }

    public async Task<ProblemDetailsView?> DeleteAsync(
        Guid folderId, bool recursive, CancellationToken cancellationToken = default)
    {
        try
        {
            await _api.Api.V1.Folders[folderId].DeleteAsync(
                request =>
                {
                    if (recursive)
                    {
                        request.QueryParameters.Recursive = true;
                    }
                },
                cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (ApiException ex)
        {
            return ex.ToProblemView();
        }
    }

    private static ProblemDetailsView EmptyResponseProblem(string title) => new()
    {
        Title = title,
        Detail = "The server returned an empty response. Try again.",
    };
}
