using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The read half of document tagging (#139): the owner reads their document's tag
/// set with each association's <c>Source</c>; a missing or cross-owner document is a
/// uniform 404, never 403 (05-security.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class GetDocumentTagsEndpointTests(FilerApiFactory factory)
{
    private const string TagsRoute = "/api/v1/tags";
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetTags_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/tags", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTags_OwnedDocument_ReturnsTheAssociationSetWithSource()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);
        Guid tagId = await CreateTagAsync(client, "urgent");
        (await client.PostAsync($"{DocumentsRoute}/{documentId}/tags/{tagId}", content: null, Ct))
            .EnsureSuccessStatusCode();

        HttpResponseMessage response = await client.GetAsync($"{DocumentsRoute}/{documentId}/tags", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DocumentTagsDto result = (await response.Content.ReadFromJsonAsync<DocumentTagsDto>(Ct))!;
        result.DocumentId.Should().Be(documentId);
        result.Tags.Should().ContainSingle(t => t.TagId == tagId)
            .Which.Source.Should().Be("User");
    }

    [Fact]
    public async Task GetTags_UntaggedDocument_ReturnsAnEmptySet()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);

        HttpResponseMessage response = await client.GetAsync($"{DocumentsRoute}/{documentId}/tags", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DocumentTagsDto result = (await response.Content.ReadFromJsonAsync<DocumentTagsDto>(Ct))!;
        result.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTags_DocumentOfAnotherOwner_Returns404()
    {
        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(owner);

        HttpClient intruder = await AuthenticatedClientAsync();
        HttpResponseMessage response = await intruder.GetAsync($"{DocumentsRoute}/{documentId}/tags", Ct);

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
            Encoding.ASCII.GetBytes($"%PDF-1.7 get-tags {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "get-tags.pdf" } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    /// <summary>The slices of the contracts these tests need, restated independently.</summary>
    private sealed record DocumentTagsDto(Guid DocumentId, IReadOnlyList<DocumentTagItemDto> Tags);

    private sealed record DocumentTagItemDto(Guid TagId, string Source);
}
