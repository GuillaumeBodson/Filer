using System.Net;
using System.Net.Http.Json;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Tags;

/// <summary>
/// The list-tags contract end to end against the real host and Postgres
/// (03-api-specification.md): the listing is owner-scoped, ordered by name, and
/// another owner's tags are absent rather than forbidden (05-security.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ListTagsEndpointTests(FilerApiFactory factory)
{
    private const string TagsRoute = "/api/v1/tags";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListTags_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(TagsRoute, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListTags_WithoutAnyTags_ReturnsAnEmptyArray()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(TagsRoute, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TagDto[] items = (await response.Content.ReadFromJsonAsync<TagDto[]>(Ct))!;
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListTags_ReturnsTheCallersTagsOrderedByName()
    {
        HttpClient client = await AuthenticatedClientAsync();
        await CreateTagAsync(client, "urgent");
        await CreateTagAsync(client, "archived");

        HttpResponseMessage response = await client.GetAsync(TagsRoute, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TagDto[] items = (await response.Content.ReadFromJsonAsync<TagDto[]>(Ct))!;
        items.Select(t => t.Name).Should().ContainInOrder("archived", "urgent");
        items.Should().OnlyContain(t => t.Id != Guid.Empty);
    }

    [Fact]
    public async Task ListTags_ReturnsOnlyTheCallersTags()
    {
        // Owner scoping is structural (05-security.md): another owner's tags are
        // absent from the listing, not forbidden.
        HttpClient first = await AuthenticatedClientAsync();
        await CreateTagAsync(first, "private");

        HttpClient second = await AuthenticatedClientAsync();
        await CreateTagAsync(second, "mine");

        HttpResponseMessage response = await second.GetAsync(TagsRoute, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TagDto[] items = (await response.Content.ReadFromJsonAsync<TagDto[]>(Ct))!;
        items.Select(t => t.Name).Should().ContainSingle().Which.Should().Be("mine");
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        return client.WithBearer(user.AccessToken);
    }

    private static async Task CreateTagAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            TagsRoute, new { name }, Ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The response contract restated independently of the module's DTO, so a
    /// breaking change surfaces as a failing test (12-testing-strategy.md).
    /// </summary>
    private sealed record TagDto(
        Guid Id,
        string Name,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
