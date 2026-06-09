using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The remove-document-tag contract end to end against the real host and Postgres
/// (03-api-specification.md, #49): the owner detaches a tag from their own
/// document — 204 — while a missing association, or a missing or cross-owner
/// document, is a uniform 404, never 403 (05-security.md) — exercised through the
/// real pipeline: JWT validation -> ICurrentUser -> owner-scoped lookup ->
/// problem-details.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RemoveDocumentTagEndpointTests(FilerApiFactory factory)
{
    private const string TagsRoute = "/api/v1/tags";
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RemoveTag_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.DeleteAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/tags/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RemoveTag_ExistingAssociation_Returns204()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);
        Guid tagId = await CreateTagAsync(client, "urgent");
        await AddTagAsync(client, documentId, tagId);

        HttpResponseMessage response = await client.DeleteAsync(
            $"{DocumentsRoute}/{documentId}/tags/{tagId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The association is gone: the document no longer matches a tag filter.
        PagedDocuments filtered = await ListByTagAsync(client, tagId);
        filtered.Items.Should().NotContain(d => d.Id == documentId);
    }

    [Fact]
    public async Task RemoveTag_RepeatedRemove_Returns404()
    {
        // The association is hard-deleted, so a second remove finds nothing — the
        // same uniform 404 as a never-existing association (05-security.md).
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);
        Guid tagId = await CreateTagAsync(client, "urgent");
        await AddTagAsync(client, documentId, tagId);

        (await client.DeleteAsync($"{DocumentsRoute}/{documentId}/tags/{tagId}", Ct))
            .EnsureSuccessStatusCode();

        HttpResponseMessage repeat = await client.DeleteAsync(
            $"{DocumentsRoute}/{documentId}/tags/{tagId}", Ct);
        repeat.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveTag_AssociationNeverExisted_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);
        Guid tagId = await CreateTagAsync(client, "never-attached");

        HttpResponseMessage response = await client.DeleteAsync(
            $"{DocumentsRoute}/{documentId}/tags/{tagId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveTag_UnknownDocument_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(client, "urgent");

        HttpResponseMessage response = await client.DeleteAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/tags/{tagId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveTag_OnDocumentOfAnotherOwner_Returns404AndKeepsAssociation()
    {
        // The required security test: owner A's document must be invisible to
        // owner B — not 403, and the association must survive the attempt.
        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(owner);
        Guid tagId = await CreateTagAsync(owner, "private");
        await AddTagAsync(owner, documentId, tagId);

        HttpClient intruder = await AuthenticatedClientAsync();
        HttpResponseMessage response = await intruder.DeleteAsync(
            $"{DocumentsRoute}/{documentId}/tags/{tagId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And the association survives: the owner still finds the document by tag.
        PagedDocuments filtered = await ListByTagAsync(owner, tagId);
        filtered.Items.Should().ContainSingle(d => d.Id == documentId);
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
            Encoding.ASCII.GetBytes($"%PDF-1.7 remove-tag {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "remove-tag.pdf" } };

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
