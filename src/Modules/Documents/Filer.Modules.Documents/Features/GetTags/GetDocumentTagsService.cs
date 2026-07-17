using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.ReplaceTags;
using Filer.Modules.Documents.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Documents.Features.GetTags;

/// <summary>
/// The read half of document tagging (#139): the current tag set with each
/// association's <c>Source</c> (User vs AiSuggested, 02-data-model.md), reusing the
/// replace slice's DTO like the mutation slices do. Missing and cross-owner are a
/// uniform 404 (05-security.md).
/// </summary>
public sealed class GetDocumentTagsService(
    IDocumentStore documents,
    ICurrentUser currentUser)
{
    public async Task<Result<DocumentTagsResponse>> HandleAsync(
        Guid documentId, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership check below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<DocumentTagsResponse>(Error.Unauthorized());
        }

        Document? document = await documents.FindActiveByIdAsync(
            currentUser.Id, documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<DocumentTagsResponse>(
                Error.NotFound("The document was not found.", DocumentsErrorCodes.DocumentNotFound));
        }

        IReadOnlyList<DocumentTag> tags = await documents.ListTagsForDocumentAsync(
            documentId, cancellationToken);

        return Result.Success(DocumentTagsResponse.From(documentId, tags));
    }
}
