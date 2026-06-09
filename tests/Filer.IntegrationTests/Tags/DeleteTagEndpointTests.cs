using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Tags;

/// <summary>
/// The delete-tag contract end to end against the real host and Postgres
/// (03-api-specification.md, #48): the owner hard-deletes their own tag — 204 —
/// and the cascade removes the tag's document associations (ADR-009); a missing or
/// cross-owner tag is a uniform 404, never 403 (05-security.md) — the AC's
/// required security guarantee, exercised through the real pipeline: JWT
/// validation -> ICurrentUser -> owner-scoped lookup -> cross-module cascade ->
/// problem-details.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DeleteTagEndpointTests(FilerApiFactory factory)
{
    private const string TagsRoute = "/api/v1/tags";
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeleteTag_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.DeleteAsync($"{TagsRoute}/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteTag_OwnedTag_Returns204()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(client, "urgent");

        HttpResponseMessage response = await client.DeleteAsync($"{TagsRoute}/{tagId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteTag_RepeatedDelete_Returns404()
    {
        // Tags are hard-deleted (Tag.cs), so a second delete finds nothing — the
        // same uniform 404 as a never-existing tag (05-security.md).
        HttpClient client = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(client, "urgent");

        (await client.DeleteAsync($"{TagsRoute}/{tagId}", Ct)).EnsureSuccessStatusCode();

        HttpResponseMessage repeat = await client.DeleteAsync($"{TagsRoute}/{tagId}", Ct);
        repeat.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteTag_OfAnotherOwner_Returns404()
    {
        // The required security test: owner A's tag must be invisible to owner B —
        // not 403 (which would confirm it exists), not a successful delete.
        HttpClient owner = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(owner, "urgent");

        HttpClient intruder = await AuthenticatedClientAsync();
        HttpResponseMessage response = await intruder.DeleteAsync($"{TagsRoute}/{tagId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And the tag survives: the cross-owner delete must not have touched it.
        HttpResponseMessage ownerDelete = await owner.DeleteAsync($"{TagsRoute}/{tagId}", Ct);
        ownerDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteTag_RemovesItsDocumentAssociations()
    {
        // The cascade (ADR-009): a document carrying the tag must no longer match a
        // tag filter once the tag is deleted — its DocumentTag row went with it.
        HttpClient client = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(client, "urgent");
        Guid documentId = await UploadDocumentAsync(client);

        HttpResponseMessage add = await client.PostAsync(
            $"{DocumentsRoute}/{documentId}/tags/{tagId}", content: null, Ct);
        add.EnsureSuccessStatusCode();

        // The association exists: the tag filter returns the document.
        PagedDocuments before = await ListByTagAsync(client, tagId);
        before.Items.Should().ContainSingle(d => d.Id == documentId);

        HttpResponseMessage delete = await client.DeleteAsync($"{TagsRoute}/{tagId}", Ct);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The association is gone with the tag: the document no longer matches.
        PagedDocuments after = await ListByTagAsync(client, tagId);
        after.Items.Should().NotContain(d => d.Id == documentId);

        // The document itself survives — only the association was removed.
        HttpResponseMessage doc = await client.GetAsync($"{DocumentsRoute}/{documentId}", Ct);
        doc.StatusCode.Should().Be(HttpStatusCode.OK);
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

    private static async Task<Guid> UploadDocumentAsync(HttpClient client)
    {
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 delete-tag cascade {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "cascade.pdf" } };

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

    private sealed record DocumentListItem(Guid Id);

    private sealed record PagedDocuments(IReadOnlyList<DocumentListItem> Items, long TotalCount);
}
