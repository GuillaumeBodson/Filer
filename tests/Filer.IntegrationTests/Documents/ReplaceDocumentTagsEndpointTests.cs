using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The replace-document-tags contract end to end against the real host and
/// Postgres (03-api-specification.md, #49, ADR-009): PUT sets the document's
/// User-sourced tag set to exactly the supplied ids — 200 with the resulting set,
/// an empty list clears the user tags — while a null list is 400 and a missing or
/// cross-owner document or tag a uniform 404, never 403 (05-security.md) —
/// exercised through the real pipeline: JWT validation -> ICurrentUser ->
/// owner-scoped lookup -> cross-module tag ownership check -> problem-details.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ReplaceDocumentTagsEndpointTests(FilerApiFactory factory)
{
    private const string TagsRoute = "/api/v1/tags";
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReplaceTags_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/tags", new { tagIds = Array.Empty<Guid>() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReplaceTags_SetsExactlyTheSuppliedSet()
    {
        // The document starts with one tag; the replace swaps it for another. The
        // response — and a follow-up tag filter — must reflect only the new set.
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);
        Guid oldTagId = await CreateTagAsync(client, "old");
        Guid newTagId = await CreateTagAsync(client, "new");
        await AddTagAsync(client, documentId, oldTagId);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/tags", new { tagIds = new[] { newTagId } }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DocumentTagsDto result = (await response.Content.ReadFromJsonAsync<DocumentTagsDto>(Ct))!;
        result.DocumentId.Should().Be(documentId);
        result.Tags.Should().ContainSingle(t => t.TagId == newTagId)
            .Which.Source.Should().Be("User");
        result.Tags.Should().NotContain(t => t.TagId == oldTagId);

        // The old association is really gone end to end, not just in the response.
        PagedDocuments byOldTag = await ListByTagAsync(client, oldTagId);
        byOldTag.Items.Should().NotContain(d => d.Id == documentId);
    }

    [Fact]
    public async Task ReplaceTags_EmptyList_ClearsUserTags()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);
        Guid tagId = await CreateTagAsync(client, "urgent");
        await AddTagAsync(client, documentId, tagId);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/tags", new { tagIds = Array.Empty<Guid>() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DocumentTagsDto result = (await response.Content.ReadFromJsonAsync<DocumentTagsDto>(Ct))!;
        result.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplaceTags_NullTagIds_Returns400()
    {
        // A null list is malformed (ReplaceDocumentTagsRequest): distinct from an
        // empty list, which is a legitimate clear.
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/tags", new { tagIds = (Guid[]?)null }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReplaceTags_UnknownDocument_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(client, "urgent");

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/tags", new { tagIds = new[] { tagId } }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReplaceTags_OnDocumentOfAnotherOwner_Returns404()
    {
        // The required security test: owner A's document must be invisible to
        // owner B — not 403 (which would confirm it exists), not a replace.
        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(owner);

        HttpClient intruder = await AuthenticatedClientAsync();
        HttpResponseMessage response = await intruder.PutAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/tags", new { tagIds = Array.Empty<Guid>() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReplaceTags_WithTagOfAnotherOwner_Returns404AndChangesNothing()
    {
        // The same uniform-404 rule applied to the tag side: one foreign tag in
        // the set fails the whole replace, leaving the current set untouched.
        HttpClient other = await AuthenticatedClientAsync();
        Guid foreignTagId = await CreateTagAsync(other, "theirs");

        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(owner);
        Guid ownTagId = await CreateTagAsync(owner, "mine");
        await AddTagAsync(owner, documentId, ownTagId);

        HttpResponseMessage response = await owner.PutAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/tags", new { tagIds = new[] { foreignTagId } }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And the existing association survives the failed replace.
        PagedDocuments byOwnTag = await ListByTagAsync(owner, ownTagId);
        byOwnTag.Items.Should().ContainSingle(d => d.Id == documentId);
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        return client.WithBearer(user.AccessToken);
    }

    private static async Task<Guid> CreateTagAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(TagsRoute, new { name }, Ct);
        response.EnsureSuccessStatusCode();
        TagDto created = (await response.Content.ReadFromJsonAsync<TagDto>(Ct))!;
        return created.Id;
    }

    private static async Task AddTagAsync(HttpClient client, Guid documentId, Guid tagId)
    {
        HttpResponseMessage response = await client.PostAsync(
            $"{DocumentsRoute}/{documentId}/tags/{tagId}", content: null, Ct);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> UploadDocumentAsync(HttpClient client)
    {
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 replace-tags {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "replace-tags.pdf" } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    private static async Task<PagedDocuments> ListByTagAsync(HttpClient client, Guid tagId)
    {
        HttpResponseMessage response = await client.GetAsync($"{DocumentsRoute}?tagId={tagId}", Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedDocuments>(Ct))!;
    }

    /// <summary>The slices of the contracts these tests need, restated independently.</summary>
    private sealed record TagDto(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private sealed record UploadResult(Guid Id);

    private sealed record DocumentTagsDto(Guid DocumentId, IReadOnlyList<DocumentTagItemDto> Tags);

    private sealed record DocumentTagItemDto(Guid TagId, string Source);

    private sealed record DocumentListItem(Guid Id);

    private sealed record PagedDocuments(IReadOnlyList<DocumentListItem> Items, long TotalCount);
}
