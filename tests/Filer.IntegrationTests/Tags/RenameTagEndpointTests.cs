using System.Net;
using System.Net.Http.Json;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Tags;

/// <summary>
/// The rename-tag contract end to end against the real host and Postgres
/// (03-api-specification.md, #47): the owner renames their own tag — 200 with the
/// updated representation — a duplicate name among the owner's tags is 409, an
/// invalid name 400, and a missing or cross-owner tag a uniform 404, never 403
/// (05-security.md) — exercised through the real pipeline: JWT validation ->
/// ICurrentUser -> owner-scoped lookup -> problem-details.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RenameTagEndpointTests(FilerApiFactory factory)
{
    private const string TagsRoute = "/api/v1/tags";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RenameTag_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{TagsRoute}/{Guid.NewGuid()}", new { name = "renamed" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RenameTag_OwnedTag_Returns200WithNewName()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(client, "draft");

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{TagsRoute}/{tagId}", new { name = "final" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        TagDto renamed = (await response.Content.ReadFromJsonAsync<TagDto>(Ct))!;
        renamed.Id.Should().Be(tagId);
        renamed.Name.Should().Be("final");
    }

    [Fact]
    public async Task RenameTag_UnknownTag_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{TagsRoute}/{Guid.NewGuid()}", new { name = "renamed" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RenameTag_OfAnotherOwner_Returns404AndLeavesTagUntouched()
    {
        // The required security test: owner A's tag must be invisible to owner B —
        // not 403 (which would confirm it exists), not a successful rename.
        HttpClient owner = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(owner, "private");

        HttpClient intruder = await AuthenticatedClientAsync();
        HttpResponseMessage response = await intruder.PatchAsJsonAsync(
            $"{TagsRoute}/{tagId}", new { name = "hijacked" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And the tag is untouched: the owner still sees the original name.
        TagDto[] tags = (await owner.GetFromJsonAsync<TagDto[]>(TagsRoute, Ct))!;
        tags.Should().ContainSingle(t => t.Id == tagId)
            .Which.Name.Should().Be("private");
    }

    [Fact]
    public async Task RenameTag_ToNameOfAnotherOwnedTag_Returns409()
    {
        HttpClient client = await AuthenticatedClientAsync();
        await CreateTagAsync(client, "existing");
        Guid tagId = await CreateTagAsync(client, "draft");

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{TagsRoute}/{tagId}", new { name = "existing" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RenameTag_BlankName_Returns400()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid tagId = await CreateTagAsync(client, "draft");

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{TagsRoute}/{tagId}", new { name = "   " }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
}
