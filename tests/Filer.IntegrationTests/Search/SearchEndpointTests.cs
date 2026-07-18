using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.Search;

/// <summary>
/// The search contract end to end against the real host and Postgres
/// (03-api-specification.md, Search): ranked full-text over the caller's
/// documents through the generated tsvector column and its GIN index —
/// owner-scoped, soft-deleted excluded, prefix matching on the last token,
/// file-name matches ranked above metadata matches, and 400s on invalid input.
/// Every test registers its own account, so owner scoping also isolates test
/// data on the shared database.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class SearchEndpointTests(FilerApiFactory factory)
{
    private const string SearchRoute = "/api/v1/search";

    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"{SearchRoute}?q=facture", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_ReturnsOnlyTheCallersMatches()
    {
        HttpClient owner = await AuthenticatedClientAsync();
        HttpClient stranger = await AuthenticatedClientAsync();

        // Unique term per run: the database is shared across tests (no reset).
        string term = UniqueTerm();
        Guid ownedId = await UploadAsync(owner, $"{term}-invoice.pdf");
        await UploadAsync(stranger, $"{term}-invoice.pdf");

        Envelope envelope = await GetEnvelopeAsync(owner, $"{SearchRoute}?q={term}");

        envelope.Page.Should().Be(1);
        envelope.PageSize.Should().Be(20, "defaults apply when paging is omitted");
        envelope.TotalCount.Should().Be(1, "the envelope is scoped to the owner (05-security.md)");
        envelope.Items.Should().ContainSingle().Which.Id.Should().Be(ownedId);
    }

    [Fact]
    public async Task Search_ExcludesSoftDeletedDocuments()
    {
        HttpClient client = await AuthenticatedClientAsync();

        string term = UniqueTerm();
        Guid keptId = await UploadAsync(client, $"{term}-kept.pdf");
        Guid deletedId = await UploadAsync(client, $"{term}-deleted.pdf");
        await SoftDeleteAsync(client, deletedId);

        Envelope envelope = await GetEnvelopeAsync(client, $"{SearchRoute}?q={term}");

        envelope.TotalCount.Should().Be(1);
        envelope.Items.Select(i => i.Id).Should().ContainSingle().Which.Should().Be(keptId);
    }

    [Fact]
    public async Task Search_MatchesTheLastTokenByPrefix()
    {
        HttpClient client = await AuthenticatedClientAsync();

        string term = UniqueTerm();
        Guid matchId = await UploadAsync(client, $"{term}ture_2024.pdf");
        await UploadAsync(client, "unrelated.pdf");

        // 'simple' has no stemming; the trailing token matches by prefix instead
        // ("{term}" finds "{term}ture_2024") — the search-as-you-type behavior.
        Envelope envelope = await GetEnvelopeAsync(client, $"{SearchRoute}?q={term}");

        envelope.TotalCount.Should().Be(1);
        envelope.Items.Should().ContainSingle().Which.Id.Should().Be(matchId);
    }

    [Fact]
    public async Task Search_MatchesJsonbMetadataValues()
    {
        HttpClient client = await AuthenticatedClientAsync();

        string term = UniqueTerm();
        Guid taggedId = await UploadAsync(client, "plain-name.pdf");
        await SetMetadataAsync(taggedId, $$"""{"title": "{{term}} budget"}""");

        Envelope envelope = await GetEnvelopeAsync(client, $"{SearchRoute}?q={term}");

        envelope.TotalCount.Should().Be(1);
        envelope.Items.Should().ContainSingle().Which.Id.Should().Be(taggedId);
    }

    [Fact]
    public async Task Search_RanksFileNameMatchesAboveMetadataMatches()
    {
        HttpClient client = await AuthenticatedClientAsync();

        string term = UniqueTerm();
        Guid metadataMatchId = await UploadAsync(client, "notes.pdf");
        await SetMetadataAsync(metadataMatchId, $$"""{"title": "{{term}}"}""");
        Guid fileNameMatchId = await UploadAsync(client, $"{term}.pdf");

        Envelope envelope = await GetEnvelopeAsync(client, $"{SearchRoute}?q={term}");

        envelope.TotalCount.Should().Be(2);
        // File-name lexemes carry weight A, metadata weight B (02-data-model.md).
        envelope.Items.Select(i => i.Id).Should().Equal(fileNameMatchId, metadataMatchId);
        envelope.Items[0].Score.Should().BeGreaterThan(envelope.Items[1].Score,
            "the opaque score orders a single response, highest first");
    }

    [Fact]
    public async Task Search_PaginationSplitsTheRankedResultWithoutOverlapOrLoss()
    {
        HttpClient client = await AuthenticatedClientAsync();

        string term = UniqueTerm();
        var uploaded = new HashSet<Guid>
        {
            await UploadAsync(client, $"{term}-one.pdf"),
            await UploadAsync(client, $"{term}-two.pdf"),
            await UploadAsync(client, $"{term}-three.pdf"),
        };

        Envelope first = await GetEnvelopeAsync(client, $"{SearchRoute}?q={term}&page=1&pageSize=2");
        Envelope second = await GetEnvelopeAsync(client, $"{SearchRoute}?q={term}&page=2&pageSize=2");

        first.TotalCount.Should().Be(3);
        first.Items.Should().HaveCount(2);
        second.TotalCount.Should().Be(3);
        second.Items.Should().HaveCount(1);

        first.Items.Concat(second.Items).Select(i => i.Id)
            .Should().BeEquivalentTo(uploaded, "pages partition the ranked result set");
    }

    [Fact]
    public async Task Search_WithAPunctuationOnlyTerm_ReturnsAnEmptyPage()
    {
        HttpClient client = await AuthenticatedClientAsync();
        await UploadAsync(client, "anything.pdf");

        // No lexeme can come out of '...' — an empty page, not an error.
        Envelope envelope = await GetEnvelopeAsync(client, $"{SearchRoute}?q=...");

        envelope.TotalCount.Should().Be(0);
        envelope.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "search_term_invalid")]
    [InlineData("?q=", "search_term_invalid")]
    [InlineData("?q=facture&page=0", "page_invalid")]
    [InlineData("?q=facture&pageSize=0", "page_size_invalid")]
    [InlineData("?q=facture&pageSize=101", "page_size_invalid")]
    public async Task Search_WithInvalidInput_Returns400WithTheStableErrorCode(
        string queryString, string expectedCode)
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(SearchRoute + queryString, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be(expectedCode);
    }

    [Fact]
    public async Task Search_WithAnOverlongTerm_Returns400WithTheStableErrorCode()
    {
        HttpClient client = await AuthenticatedClientAsync();
        string term = new('a', 256);

        HttpResponseMessage response = await client.GetAsync($"{SearchRoute}?q={term}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("search_term_invalid");
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        return client.WithBearer(user.AccessToken);
    }

    /// <summary>
    /// A letters-only token unique per call, so full-text matches never leak
    /// across tests sharing the one database.
    /// </summary>
    private static string UniqueTerm() => $"term{Guid.NewGuid():N}";

    private static async Task<Envelope> GetEnvelopeAsync(HttpClient client, string uri)
    {
        HttpResponseMessage response = await client.GetAsync(uri, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<Envelope>(Ct))!;
    }

    /// <summary>
    /// No API writes <c>Metadata</c> yet; arranged through the module's
    /// DbContext, like the folder arrangement in the list tests. The generated
    /// search vector recomputes on this update.
    /// </summary>
    private async Task SetMetadataAsync(Guid documentId, string metadataJson)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        Document document = await db.Documents.SingleAsync(d => d.Id == documentId, Ct);
        document.Metadata = metadataJson;
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>Deletion through the public DELETE endpoint, as a client would (#38).</summary>
    private static async Task SoftDeleteAsync(HttpClient client, Guid documentId)
    {
        HttpResponseMessage response = await client.DeleteAsync($"{DocumentsRoute}/{documentId}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string fileName)
    {
        // Unique bytes per call so tests never collide on the dedupe index.
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 search test content {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    /// <summary>The paged envelope contract, restated independently of the module's DTOs (12-testing-strategy.md).</summary>
    private sealed record Envelope(List<SearchHit> Items, int Page, int PageSize, long TotalCount);

    /// <summary>The slice of a search hit these tests need.</summary>
    private sealed record SearchHit(Guid Id, Guid? FolderId, string FileName, double Score);
}
