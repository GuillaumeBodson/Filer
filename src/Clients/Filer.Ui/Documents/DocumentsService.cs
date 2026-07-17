using Filer.ApiClient.Generated;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;
using Microsoft.Kiota.Abstractions;

namespace Filer.Ui.Documents;

/// <summary>Default <see cref="IDocumentsService"/> over the generated client.</summary>
public sealed class DocumentsService(FilerApiClient api) : IDocumentsService
{
    private readonly FilerApiClient _api = api;

    public async Task<DocumentsPageResult> ListAsync(
        DocumentsQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            PagedResultOfDocumentListItemResponse? page = await _api.Api.V1.Documents.GetAsync(
                request =>
                {
                    request.QueryParameters.FolderId = query.FolderId;
                    request.QueryParameters.TagId = query.TagId;
                    request.QueryParameters.Q = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text;
                    request.QueryParameters.Page = query.Page;
                    request.QueryParameters.PageSize = query.PageSize;
                },
                cancellationToken).ConfigureAwait(false);

            return page is null
                ? new DocumentsPageResult(null, new ProblemDetailsView
                {
                    Title = "Documents unavailable",
                    Detail = "The server returned an empty response. Try again.",
                })
                : new DocumentsPageResult(page, null);
        }
        catch (ApiException ex)
        {
            return new DocumentsPageResult(null, ex.ToProblemView());
        }
    }

    public async Task<DocumentUploadResult> UploadAsync(
        DocumentUploadRequest upload, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new MultipartBody();
            body.AddOrReplacePart("file", upload.ContentType, upload.Content, upload.FileName);

            UploadDocumentResponse? created = await _api.Api.V1.Documents.PostAsync(
                body, cancellationToken: cancellationToken).ConfigureAwait(false);

            return created is null
                ? new DocumentUploadResult(null, new ProblemDetailsView
                {
                    Title = "Upload failed",
                    Detail = "The server returned an empty response. Try again.",
                })
                : new DocumentUploadResult(created, null);
        }
        catch (ApiException ex)
        {
            // The duplicate-content 409 carries the existing document's id (03).
            Guid? duplicateOf = Guid.TryParse(ex.GetExtensionString("existingDocumentId"), out Guid id)
                ? id
                : null;
            return new DocumentUploadResult(null, ex.ToProblemView(), duplicateOf);
        }
    }

    public async Task<DocumentUpdateResult> UpdateAsync(
        Guid documentId, DocumentUpdate update, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new UpdateDocumentMetadataRequest();
            if (update.NewFileName is not null)
            {
                body.FileName = update.NewFileName;
            }

            if (update.MoveFolder)
            {
                if (update.TargetFolderId is Guid target)
                {
                    body.FolderId = target;
                }
                else
                {
                    // Merge-patch: an explicit "folderId": null moves to the root; the
                    // generated writer skips null typed properties, AdditionalData not.
                    body.AdditionalData["folderId"] = null!;
                }
            }

            UpdateDocumentMetadataResponse? updated = await _api.Api.V1.Documents[documentId]
                .PatchAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

            return updated is null
                ? new DocumentUpdateResult(null, new ProblemDetailsView
                {
                    Title = "Update failed",
                    Detail = "The server returned an empty response. Try again.",
                })
                : new DocumentUpdateResult(updated, null);
        }
        catch (ApiException ex)
        {
            return new DocumentUpdateResult(null, ex.ToProblemView());
        }
    }

    public async Task<DocumentContentResult> DownloadAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            Stream? content = await _api.Api.V1.Documents[documentId].Content
                .GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            if (content is null)
            {
                return new DocumentContentResult(null, new ProblemDetailsView
                {
                    Title = "Download failed",
                    Detail = "The server returned no content. Try again.",
                });
            }

            await using (content.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                return new DocumentContentResult(buffer.ToArray(), null);
            }
        }
        catch (ApiException ex)
        {
            return new DocumentContentResult(null, ex.ToProblemView());
        }
    }

    public async Task<ProblemDetailsView?> DeleteAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _api.Api.V1.Documents[documentId]
                .DeleteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (ApiException ex)
        {
            return ex.ToProblemView();
        }
    }

    public async Task<DocumentMetadataResult> GetMetadataAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            DocumentMetadataResponse? document = await _api.Api.V1.Documents[documentId]
                .GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            return document is null
                ? new DocumentMetadataResult(null, new ProblemDetailsView
                {
                    Title = "Document unavailable",
                    Detail = "The server returned an empty response. Try again.",
                })
                : new DocumentMetadataResult(document, null);
        }
        catch (ApiException ex)
        {
            return new DocumentMetadataResult(null, ex.ToProblemView());
        }
    }
}
