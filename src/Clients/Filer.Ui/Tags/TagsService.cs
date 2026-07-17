using Filer.ApiClient.Generated;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;
using Microsoft.Kiota.Abstractions;

namespace Filer.Ui.Tags;

/// <summary>Default <see cref="ITagsService"/> over the generated client.</summary>
public sealed class TagsService(FilerApiClient api) : ITagsService
{
    private readonly FilerApiClient _api = api;

    public async Task<TagsListResult> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            List<TagListItemResponse>? tags = await _api.Api.V1.Tags.GetAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return new TagsListResult(tags ?? [], null);
        }
        catch (ApiException ex)
        {
            return new TagsListResult(null, ex.ToProblemView());
        }
    }

    public async Task<TagMutationResult> CreateAsync(
        string name, CancellationToken cancellationToken = default)
    {
        try
        {
            CreateTagResponse? created = await _api.Api.V1.Tags.PostAsync(
                new CreateTagRequest { Name = name },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return created?.Id is null
                ? new TagMutationResult(null, null, EmptyResponseProblem())
                : new TagMutationResult(created.Id, created.Name, null);
        }
        catch (ApiException ex)
        {
            return new TagMutationResult(null, null, ex.ToProblemView());
        }
    }

    public async Task<TagMutationResult> RenameAsync(
        Guid tagId, string name, CancellationToken cancellationToken = default)
    {
        try
        {
            RenameTagResponse? renamed = await _api.Api.V1.Tags[tagId].PatchAsync(
                new RenameTagRequest { Name = name },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return renamed?.Id is null
                ? new TagMutationResult(null, null, EmptyResponseProblem())
                : new TagMutationResult(renamed.Id, renamed.Name, null);
        }
        catch (ApiException ex)
        {
            return new TagMutationResult(null, null, ex.ToProblemView());
        }
    }

    public async Task<ProblemDetailsView?> DeleteAsync(
        Guid tagId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _api.Api.V1.Tags[tagId].DeleteAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (ApiException ex)
        {
            return ex.ToProblemView();
        }
    }

    private static ProblemDetailsView EmptyResponseProblem() => new()
    {
        Title = "Tag operation failed",
        Detail = "The server returned an empty response. Try again.",
    };
}
