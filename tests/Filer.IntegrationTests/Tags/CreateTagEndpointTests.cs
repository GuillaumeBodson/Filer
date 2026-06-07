using System.Net;
using System.Net.Http.Json;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Filer.IntegrationTests.Tags;

/// <summary>
/// The create-tag contract end to end against the real host and Postgres
/// (03-api-specification.md): the owner creates tags, names are unique per owner
/// — 409 on clash (02-data-model.md) — and the same name remains free for other
/// owners.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CreateTagEndpointTests(FilerApiFactory factory)
{
    private const string TagsRoute = "/api/v1/tags";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateTag_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            TagsRoute, new { name = "urgent" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateTag_WithAFreeName_Returns201WithLocationAndDto()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            TagsRoute, new { name = "urgent" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        TagDto created = (await response.Content.ReadFromJsonAsync<TagDto>(Ct))!;
        created.Id.Should().NotBeEmpty();
        created.Name.Should().Be("urgent");
        created.UpdatedAt.Should().Be(created.CreatedAt);

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should().Be($"{TagsRoute}/{created.Id}");
    }

    [Fact]
    public async Task CreateTag_DuplicateName_Returns409NameConflict()
    {
        HttpClient client = await AuthenticatedClientAsync();
        await CreateTagAsync(client, "urgent");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            TagsRoute, new { name = "urgent" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be("tag_name_conflict");
    }

    [Fact]
    public async Task CreateTag_SameNameByAnotherOwner_Returns201()
    {
        // Uniqueness is owner-scoped: two users may both have "urgent".
        HttpClient first = await AuthenticatedClientAsync();
        await CreateTagAsync(first, "urgent");

        HttpClient second = await AuthenticatedClientAsync();
        HttpResponseMessage response = await second.PostAsJsonAsync(
            TagsRoute, new { name = "urgent" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":""}""")]
    [InlineData("""{"name":"   "}""")]
    public async Task CreateTag_MissingOrBlankName_Returns400NameInvalid(string json)
    {
        HttpClient client = await AuthenticatedClientAsync();

        using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(TagsRoute, body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be("tag_name_invalid");
    }

    [Fact]
    public async Task CreateTag_NameIsTrimmedBeforeUniquenessAndPersistence()
    {
        HttpClient client = await AuthenticatedClientAsync();
        await CreateTagAsync(client, "urgent");

        // The padded form collides with the existing trimmed tag.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            TagsRoute, new { name = "  urgent " }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        return client.WithBearer(user.AccessToken);
    }

    private static async Task<Guid> CreateTagAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            TagsRoute, new { name }, Ct);
        response.EnsureSuccessStatusCode();
        TagDto created = (await response.Content.ReadFromJsonAsync<TagDto>(Ct))!;
        return created.Id;
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
