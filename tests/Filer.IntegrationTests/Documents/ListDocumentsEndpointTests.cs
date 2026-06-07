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

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The list contract end to end against the real host and Postgres
/// (03-api-specification.md, List filters): a paged envelope scoped to the
/// caller, with folderId/tagId/q filters, soft-deleted rows excluded, and 400 on
/// bad paging input. Every test registers its own account, so owner scoping also
/// isolates test data on the shared database.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ListDocumentsEndpointTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(DocumentsRoute, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_ReturnsAPagedEnvelopeWithOnlyTheCallersDocuments()
    {
        HttpClient owner = await AuthenticatedClientAsync();
        HttpClient stranger = await AuthenticatedClientAsync();

        Guid ownedId = await UploadAsync(owner, "mine.pdf");
        await UploadAsync(stranger, "theirs.pdf");

        Envelope envelope = await GetEnvelopeAsync(owner, DocumentsRoute);

        envelope.Page.Should().Be(1);
        envelope.PageSize.Should().Be(20, "defaults apply when paging is omitted");
        envelope.TotalCount.Should().Be(1, "the envelope is scoped to the owner (05-security.md)");
        envelope.Items.Should().ContainSingle().Which.Id.Should().Be(ownedId);
    }

    [Fact]
    public async Task List_ExcludesSoftDeletedDocuments()
    {
        HttpClient client = await AuthenticatedClientAsync();

        Guid keptId = await UploadAsync(client, "kept.pdf");
        Guid deletedId = await UploadAsync(client, "deleted.pdf");
        await SoftDeleteAsync(deletedId);

        Envelope envelope = await GetEnvelopeAsync(client, DocumentsRoute);

        envelope.TotalCount.Should().Be(1);
        envelope.Items.Select(i => i.Id).Should().ContainSingle().Which.Should().Be(keptId);
    }

    [Fact]
    public async Task List_FolderIdFilter_ReturnsOnlyThatFoldersDocuments()
    {
        HttpClient client = await AuthenticatedClientAsync();

        Guid filedId = await UploadAsync(client, "filed.pdf");
        await UploadAsync(client, "unfiled.pdf");

        // No folders API exists yet (M4); arrange the folder through the module's
        // DbContext, like the soft-delete arrangement below.
        var folderId = Guid.NewGuid();
        await SetFolderAsync(filedId, folderId);

        Envelope envelope = await GetEnvelopeAsync(client, $"{DocumentsRoute}?folderId={folderId}");

        envelope.TotalCount.Should().Be(1);
        envelope.Items.Should().ContainSingle().Which.Id.Should().Be(filedId);
    }

    [Fact]
    public async Task List_SearchTerm_MatchesFileNamesCaseInsensitivelyAndLiterally()
    {
        HttpClient client = await AuthenticatedClientAsync();

        Guid underscoreId = await UploadAsync(client, "Alpha_Report.pdf");
        await UploadAsync(client, "AlphaXReport.pdf");
        await UploadAsync(client, "unrelated.pdf");

        // 'a_r' must match the literal underscore only — never act as the LIKE
        // single-character wildcard — and matching ignores case (ILIKE).
        Envelope envelope = await GetEnvelopeAsync(client, $"{DocumentsRoute}?q=a_r");

        envelope.TotalCount.Should().Be(1);
        envelope.Items.Should().ContainSingle().Which.Id.Should().Be(underscoreId);
    }

    [Fact]
    public async Task List_TagIdFilter_ReturnsAnEmptyPageUntilTagsExist()
    {
        HttpClient client = await AuthenticatedClientAsync();
        await UploadAsync(client, "untagged.pdf");

        // Tags land in M4 (#41–#45); no document carries any tag yet, so a tag
        // filter matches nothing while the contract stays in place (03).
        Envelope envelope = await GetEnvelopeAsync(client, $"{DocumentsRoute}?tagId={Guid.NewGuid()}");

        envelope.TotalCount.Should().Be(0);
        envelope.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task List_PaginationSplitsTheResultWithoutOverlapOrLoss()
    {
        HttpClient client = await AuthenticatedClientAsync();

        var uploaded = new HashSet<Guid>
        {
            await UploadAsync(client, "one.pdf"),
            await UploadAsync(client, "two.pdf"),
            await UploadAsync(client, "three.pdf"),
        };

        Envelope first = await GetEnvelopeAsync(client, $"{DocumentsRoute}?page=1&pageSize=2");
        Envelope second = await GetEnvelopeAsync(client, $"{DocumentsRoute}?page=2&pageSize=2");

        first.TotalCount.Should().Be(3);
        first.Items.Should().HaveCount(2);
        second.TotalCount.Should().Be(3);
        second.Items.Should().HaveCount(1);

        first.Items.Concat(second.Items).Select(i => i.Id)
            .Should().BeEquivalentTo(uploaded, "pages partition the result set");
    }

    [Theory]
    [InlineData("?page=0", "page_invalid")]
    [InlineData("?pageSize=0", "page_size_invalid")]
    [InlineData("?pageSize=101", "page_size_invalid")]
    public async Task List_WithOutOfRangePaging_Returns400WithTheStableErrorCode(
        string queryString, string expectedCode)
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(DocumentsRoute + queryString, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("?page=abc")]
    [InlineData("?pageSize=abc")]
    [InlineData("?folderId=not-a-guid")]
    [InlineData("?tagId=not-a-guid")]
    public async Task List_WithMalformedParameters_Returns400(string queryString)
    {
        HttpClient client = await AuthenticatedClientAsync();

        // Malformed values fail minimal-API binding before the handler runs.
        HttpResponseMessage response = await client.GetAsync(DocumentsRoute + queryString, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        return client.WithBearer(user.AccessToken);
    }

    private static async Task<Envelope> GetEnvelopeAsync(HttpClient client, string uri)
    {
        HttpResponseMessage response = await client.GetAsync(uri, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<Envelope>(Ct))!;
    }

    /// <summary>
    /// No folders API exists yet (M4); arranged through the module's DbContext.
    /// Replace with the move endpoint once the update-metadata slice lands (#37).
    /// </summary>
    private async Task SetFolderAsync(Guid documentId, Guid folderId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        Document document = await db.Documents.SingleAsync(d => d.Id == documentId, Ct);
        document.FolderId = folderId;
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// No DELETE endpoint exists yet; arranged through the module's DbContext.
    /// Replace with the API call once the delete slice lands (#38).
    /// </summary>
    private async Task SoftDeleteAsync(Guid documentId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        Document document = await db.Documents.SingleAsync(d => d.Id == documentId, Ct);
        document.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string fileName)
    {
        // Unique bytes per call so tests never collide on the dedupe index.
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 list test content {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    /// <summary>The paged envelope contract, restated independently of the module's DTOs (12-testing-strategy.md).</summary>
    private sealed record Envelope(List<DocumentItem> Items, int Page, int PageSize, long TotalCount);

    /// <summary>The slice of a list item these tests need.</summary>
    private sealed record DocumentItem(Guid Id, Guid? FolderId, string FileName);

    /// <summary>The slice of the upload response these tests need.</summary>
    private sealed record UploadResult(Guid Id);
}
