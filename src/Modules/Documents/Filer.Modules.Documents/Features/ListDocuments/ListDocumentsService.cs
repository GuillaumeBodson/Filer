using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Paging;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Documents.Features.ListDocuments;

/// <summary>
/// The list slice (03-api-specification.md): validate the request, normalize it
/// into an owner-scoped <see cref="DocumentListFilter"/>, and map the store's
/// page to response DTOs. Owner scoping is structural — the filter cannot be
/// built without the caller's id — so the result can never contain another
/// owner's documents, and soft-deleted rows are excluded by the store
/// (05-security.md, 02-data-model.md).
/// </summary>
public sealed class ListDocumentsService(
    IDocumentStore documents,
    ICurrentUser currentUser,
    ILogger<ListDocumentsService> logger)
{
    public async Task<Result<PagedResult<DocumentListItemResponse>>> HandleAsync(
        ListDocumentsQuery query, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // owner-scoped filter below must never be built from an anonymous principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<PagedResult<DocumentListItemResponse>>(Error.Unauthorized());
        }

        Result validation = ListDocumentsValidator.Validate(query);
        if (validation.IsFailure)
        {
            return Result.Failure<PagedResult<DocumentListItemResponse>>(validation.Error!);
        }

        // A blank ?q= means "no filter", not "match nothing"; trimming keeps the
        // store's pattern free of accidental whitespace.
        string? searchTerm = string.IsNullOrWhiteSpace(query.SearchTerm)
            ? null
            : query.SearchTerm.Trim();

        var filter = new DocumentListFilter(
            currentUser.Id,
            query.FolderId,
            query.TagId,
            searchTerm,
            query.Page ?? ListDocumentsValidator.DefaultPage,
            query.PageSize ?? ListDocumentsValidator.DefaultPageSize);

        PagedResult<Document> page = await documents.ListActiveAsync(filter, cancellationToken);

        List<DocumentListItemResponse> items = page.Items
            .Select(DocumentListItemResponse.From)
            .ToList();

        logger.PageServed(currentUser.Id, page.Page, items.Count, page.TotalCount);

        return Result.Success(new PagedResult<DocumentListItemResponse>(
            items, page.Page, page.PageSize, page.TotalCount));
    }
}

/// <summary>
/// Log messages for <see cref="ListDocumentsService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids and counts only — never file names or search terms (05-security.md).
/// Debug level: listing is routine and high-frequency, like metadata reads.
/// </summary>
internal static partial class ListDocumentsServiceLog
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Document page {Page} served to owner {OwnerId}: {ItemCount} of {TotalCount} documents.")]
    public static partial void PageServed(
        this ILogger logger, Guid ownerId, int page, int itemCount, long totalCount);
}
