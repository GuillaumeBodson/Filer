using Filer.Modules.Documents.Contracts;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Paging;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Search.Features.SearchDocuments;

/// <summary>
/// The search slice (03-api-specification.md, Search): validate the request,
/// normalize it into an owner-scoped <see cref="DocumentSearchQuery"/>, and map
/// the ranked page to response DTOs. Owner scoping is structural — the query
/// cannot be built without the caller's id — so the result can never contain
/// another owner's documents, and soft-deleted rows are excluded by the
/// Documents implementation behind the contract (05-security.md).
/// </summary>
public sealed class SearchDocumentsService(
    IOwnerDocumentSearch search,
    ICurrentUser currentUser,
    ILogger<SearchDocumentsService> logger)
{
    public async Task<Result<PagedResult<SearchHitResponse>>> HandleAsync(
        SearchDocumentsQuery query, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // owner-scoped query below must never be built from an anonymous principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<PagedResult<SearchHitResponse>>(Error.Unauthorized());
        }

        Result validation = SearchDocumentsValidator.Validate(query);
        if (validation.IsFailure)
        {
            return Result.Failure<PagedResult<SearchHitResponse>>(validation.Error!);
        }

        var searchQuery = new DocumentSearchQuery(
            currentUser.Id,
            query.SearchTerm!.Trim(),
            query.Page ?? SearchDocumentsValidator.DefaultPage,
            query.PageSize ?? SearchDocumentsValidator.DefaultPageSize);

        PagedResult<DocumentSearchHit> page = await search.SearchAsync(searchQuery, cancellationToken);

        List<SearchHitResponse> items = page.Items
            .Select(SearchHitResponse.From)
            .ToList();

        logger.PageServed(currentUser.Id, searchQuery.Term.Length, page.Page, items.Count, page.TotalCount);

        return Result.Success(new PagedResult<SearchHitResponse>(
            items, page.Page, page.PageSize, page.TotalCount));
    }
}

/// <summary>
/// Log messages for <see cref="SearchDocumentsService"/>, co-located per the
/// house pattern: compile-time-generated and allocation-free via
/// <c>[LoggerMessage]</c>. Ids, lengths and counts only — never the search term
/// itself (05-security.md).
/// </summary>
internal static partial class SearchDocumentsServiceLog
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Search page {Page} served to owner {OwnerId}: {ItemCount} of {TotalCount} hits for a {TermLength}-char term.")]
    public static partial void PageServed(
        this ILogger logger, Guid ownerId, int termLength, int page, int itemCount, long totalCount);
}
