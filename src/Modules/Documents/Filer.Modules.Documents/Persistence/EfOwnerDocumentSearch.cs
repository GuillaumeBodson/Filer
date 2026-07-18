using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Filer.Modules.Documents.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IOwnerDocumentSearch"/> over the module's
/// context: full-text match and <c>ts_rank</c> ordering on the stored
/// <c>SearchVector</c> column (#57, 02-data-model.md). The Search module consumes
/// this through the Contracts interface only — the vector, its GIN index, and the
/// tsquery strategy stay Documents persistence internals.
/// </summary>
public sealed class EfOwnerDocumentSearch(DocumentsDbContext db) : IOwnerDocumentSearch
{
    public async Task<PagedResult<DocumentSearchHit>> SearchAsync(
        DocumentSearchQuery query, CancellationToken cancellationToken)
    {
        bool websearch = SearchTermTsQuery.UsesWebsearchSyntax(query.Term);
        string? prefixQuery = websearch ? null : SearchTermTsQuery.BuildPrefixQuery(query.Term);

        if (!websearch && prefixQuery is null)
        {
            // Punctuation-only term: no lexeme can ever match — an empty page,
            // not an error (03-api-specification.md).
            return new PagedResult<DocumentSearchHit>([], query.Page, query.PageSize, 0);
        }

        IQueryable<Document> scoped = db.Documents
            .AsNoTracking()
            .Where(d => d.OwnerId == query.OwnerId && d.DeletedAt == null);

        // Same shape either way; only the tsquery construction differs. The
        // repeated Rank call costs nothing extra — Postgres evaluates the
        // (identical) tsquery expression once per row either way.
        var matched = websearch
            ? scoped
                .Where(d => EF.Property<NpgsqlTsVector>(d, DocumentsDbContext.SearchVectorColumn)
                    .Matches(EF.Functions.WebSearchToTsQuery("simple", query.Term)))
                .Select(d => new
                {
                    Document = d,
                    Score = EF.Property<NpgsqlTsVector>(d, DocumentsDbContext.SearchVectorColumn)
                        .Rank(EF.Functions.WebSearchToTsQuery("simple", query.Term)),
                })
            : scoped
                .Where(d => EF.Property<NpgsqlTsVector>(d, DocumentsDbContext.SearchVectorColumn)
                    .Matches(EF.Functions.ToTsQuery("simple", prefixQuery!)))
                .Select(d => new
                {
                    Document = d,
                    Score = EF.Property<NpgsqlTsVector>(d, DocumentsDbContext.SearchVectorColumn)
                        .Rank(EF.Functions.ToTsQuery("simple", prefixQuery!)),
                });

        long totalCount = await matched.LongCountAsync(cancellationToken);

        // Most relevant first; CreatedAt/Id break score ties so paging stays
        // stable, mirroring EfDocumentStore.ListActiveAsync.
        var page = await matched
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Document.CreatedAt)
            .ThenBy(x => x.Document.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        List<DocumentSearchHit> hits = page
            .Select(x => new DocumentSearchHit(
                x.Document.Id,
                x.Document.FolderId,
                x.Document.FileName,
                x.Document.ContentType,
                x.Document.SizeBytes,
                x.Document.Status.ToString(),
                x.Document.CreatedAt,
                x.Document.UpdatedAt,
                x.Score))
            .ToList();

        return new PagedResult<DocumentSearchHit>(hits, query.Page, query.PageSize, totalCount);
    }
}
