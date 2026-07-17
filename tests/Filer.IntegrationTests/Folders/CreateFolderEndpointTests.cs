using System.Net;
using System.Net.Http.Json;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Filer.IntegrationTests.Folders;

/// <summary>
/// The create-folder contract end to end against the real host and Postgres
/// (03-api-specification.md): the owner creates top-level and nested folders,
/// sibling names are unique per owner — 409 on clash (02-data-model.md) — and an
/// unowned or missing parent is a uniform 404, never 403 (05-security.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CreateFolderEndpointTests(FilerApiFactory factory)
{
    private const string FoldersRoute = "/api/v1/folders";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateFolder_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            FoldersRoute, new { name = "Invoices" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateFolder_AtTopLevel_Returns201WithLocationAndDto()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            FoldersRoute, new { name = "Invoices" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        FolderDto created = (await response.Content.ReadFromJsonAsync<FolderDto>(Ct))!;
        created.Id.Should().NotBeEmpty();
        created.Name.Should().Be("Invoices");
        created.ParentId.Should().BeNull();
        created.UpdatedAt.Should().Be(created.CreatedAt);

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should().Be($"{FoldersRoute}/{created.Id}");
    }

    [Fact]
    public async Task CreateFolder_UnderAnOwnedParent_Returns201WithTheParentReference()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid parentId = await CreateFolderAsync(client, "Taxes");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            FoldersRoute, new { name = "2026", parentId }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        FolderDto created = (await response.Content.ReadFromJsonAsync<FolderDto>(Ct))!;
        created.ParentId.Should().Be(parentId);
    }

    [Fact]
    public async Task CreateFolder_WithMissingParent_Returns404ParentNotFound()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            FoldersRoute, new { name = "Orphan", parentId = Guid.NewGuid() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("parent_folder_not_found");
    }

    [Fact]
    public async Task CreateFolder_WithAnotherOwnersParent_Returns404NotForbidden()
    {
        // The uniform-404 rule (05-security.md): a cross-owner parent responds
        // exactly like a missing one, so folder ids cannot be probed.
        HttpClient ownerClient = await AuthenticatedClientAsync();
        Guid foreignParentId = await CreateFolderAsync(ownerClient, "Private");

        HttpClient otherClient = await AuthenticatedClientAsync();
        HttpResponseMessage response = await otherClient.PostAsJsonAsync(
            FoldersRoute, new { name = "Intruder", parentId = foreignParentId }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("parent_folder_not_found");
    }

    [Fact]
    public async Task CreateFolder_DuplicateSiblingName_Returns409NameConflict()
    {
        HttpClient client = await AuthenticatedClientAsync();
        await CreateFolderAsync(client, "Invoices");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            FoldersRoute, new { name = "Invoices" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_name_conflict");
    }

    [Fact]
    public async Task CreateFolder_SameNameUnderAnotherParent_Returns201()
    {
        // Uniqueness is per (OwnerId, ParentId, Name): the same name may exist
        // at the top level and inside a folder simultaneously (02-data-model.md).
        HttpClient client = await AuthenticatedClientAsync();
        Guid parentId = await CreateFolderAsync(client, "Archive");
        await CreateFolderAsync(client, "Invoices");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            FoldersRoute, new { name = "Invoices", parentId }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateFolder_SameNameByAnotherOwner_Returns201()
    {
        // Uniqueness is owner-scoped: two users may both have "Invoices".
        HttpClient first = await AuthenticatedClientAsync();
        await CreateFolderAsync(first, "Invoices");

        HttpClient second = await AuthenticatedClientAsync();
        HttpResponseMessage response = await second.PostAsJsonAsync(
            FoldersRoute, new { name = "Invoices" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":""}""")]
    [InlineData("""{"name":"   "}""")]
    public async Task CreateFolder_MissingOrBlankName_Returns400NameInvalid(string json)
    {
        HttpClient client = await AuthenticatedClientAsync();

        using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(FoldersRoute, body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_name_invalid");
    }

    [Fact]
    public async Task CreateFolder_NameIsTrimmedBeforeUniquenessAndPersistence()
    {
        HttpClient client = await AuthenticatedClientAsync();
        await CreateFolderAsync(client, "Inbox");

        // The padded form collides with the existing trimmed sibling.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            FoldersRoute, new { name = "  Inbox " }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        return client.WithBearer(user.AccessToken);
    }

    private static async Task<Guid> CreateFolderAsync(HttpClient client, string name, Guid? parentId = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            FoldersRoute, new { name, parentId }, Ct);
        response.EnsureSuccessStatusCode();
        FolderDto created = (await response.Content.ReadFromJsonAsync<FolderDto>(Ct))!;
        return created.Id;
    }

    /// <summary>
    /// The response contract restated independently of the module's DTO, so a
    /// breaking change surfaces as a failing test (12-testing-strategy.md).
    /// </summary>
    private sealed record FolderDto(
        Guid Id,
        Guid? ParentId,
        string Name,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
