using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The add-document-tag contract end to end against the real host and Postgres
/// (03-api-specification.md, #49): the owner attaches their own tag to their own
/// document — 200 with the resulting association set, idempotently — while a
/// missing or cross-owner document or tag is a uniform 404, never 403
/// (05-security.md) — exercised through the real pipeline: JWT validation ->
/// ICurrentUser -> owner-scoped lookup -> cross-module tag ownership check ->
/// problem-details.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AddDocumentTagEndpointTests(FilerApiFactory factory)
{
    private const string TagsRoute = "/api/v1/tags";
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddTag_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/tags/{Guid.NewGuid()}", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddTag_OwnedDocumentAndTag_Returns200WithUserAssociation()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);
        Guid tagId = await CreateTagAsync(client, "urgent");

        HttpResponseMessage response = await client.PostAsync(
            $"{DocumentsRoute}/{documentId}/tags/{tagId}", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DocumentTagsDto result = (await response.Content.ReadFromJsonAsync<DocumentTagsDto>(Ct))!;
        result.DocumentId.Should().Be(documentId);
        result.Tags.Should().ContainSingle(t => t.TagId == tagId)
            .Which.Source.Should().Be("User");
    }

    [Fact]
    public async Task AddTag_Twice_IsIdempotent()
    {
        // The composite (DocumentId, TagId) key collapses repeats to one row: the
        // second add succeeds and the association set still holds a single entry.
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);
        Guid tagId = await CreateTagAsync(client, "urgent");

        (await client.PostAsync($"{DocumentsRoute}/{documentId}/tags/{tagId}", content: null, Ct))
            .EnsureSuccessStatusCode();
        HttpResponseMessage repeat = await client.PostAsync(
            $"{DocumentsRoute}/{documentId}/tags/{tagId}", content: null, Ct);

        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        DocumentTagsDto result = (await repeat.Content.ReadFromJsonAsync<DocumentTagsDto>(Ct))!;
        result.Tags.Should().ContainSingle(t => t.TagId == tagId);
    }

    [Fact]
    public async Task AddTag_UnknownDocument_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(client, "urgent");

        HttpResponseMessage response = await client.PostAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/tags/{tagId}", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddTag_ToDocumentOfAnotherOwner_Returns404()
    {
        // The required security test: owner A's document must be invisible to
        // owner B — not 403 (which would confirm it exists), not a tagging.
        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(owner);

        HttpClient intruder = await AuthenticatedClientAsync();
        Guid intruderTagId = await CreateTagAsync(intruder, "mine");

        HttpResponseMessage response = await intruder.PostAsync(
            $"{DocumentsRoute}/{documentId}/tags/{intruderTagId}", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddTag_WithTagOfAnotherOwner_Returns404()
    {
        // The same uniform-404 rule applied to the tag side of the association:
        // a cross-owner tag is indistinguishable from a missing one.
        HttpClient other = await AuthenticatedClientAsync();
        Guid foreignTagId = await CreateTagAsync(other, "theirs");

        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(owner);

        HttpResponseMessage response = await owner.PostAsync(
            $"{DocumentsRoute}/{documentId}/tags/{foreignTagId}", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
            Encoding.ASCII.GetBytes($"%PDF-1.7 add-tag {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "add-tag.pdf" } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    /// <summary>The slices of the contracts these tests need, restated independently.</summary>
    private sealed record TagDto(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private sealed record UploadResult(Guid Id);

    private sealed record DocumentTagsDto(Guid DocumentId, IReadOnlyList<DocumentTagItemDto> Tags);

    private sealed record DocumentTagItemDto(Guid TagId, string Source);
}
