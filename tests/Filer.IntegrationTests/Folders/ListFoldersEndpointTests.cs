using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.Folders.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FolderEntity = Filer.Modules.Folders.Domain.Folder;

namespace Filer.IntegrationTests.Folders;

/// <summary>
/// The list-folders contract end to end against the real host and Postgres
/// (03-api-specification.md): the listing is owner-scoped, defaults to the flat
/// shape, nests the hierarchy under <c>?view=tree</c>, rejects unknown views with
/// 400, and never surfaces soft-deleted folders (05-security.md, 02-data-model.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ListFoldersEndpointTests(FilerApiFactory factory)
{
    private const string FoldersRoute = "/api/v1/folders";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListFolders_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(FoldersRoute, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListFolders_ByDefault_ReturnsTheFlatShapeWithoutAChildrenProperty()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid parentId = await CreateFolderAsync(client, "Archive");
        await CreateFolderAsync(client, "Invoices", parentId);

        HttpResponseMessage response = await client.GetAsync(FoldersRoute, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        // Both folders sit at the top level of the payload; the nested one keeps
        // its parent reference, and the flat shape omits "children" entirely.
        JsonElement[] items = [.. payload.RootElement.EnumerateArray()];
        items.Should().HaveCount(2);
        items.Select(i => i.GetProperty("name").GetString())
            .Should().ContainInOrder("Archive", "Invoices");
        items.Count(i => i.TryGetProperty("children", out _)).Should().Be(
            0, "the flat shape omits the children property entirely");

        JsonElement nested = items.Single(i => i.GetProperty("name").GetString() == "Invoices");
        nested.GetProperty("parentId").GetGuid().Should().Be(parentId);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("TREE")]
    public async Task ListFolders_WithTheTreeView_NestsChildrenUnderTheirParents(string view)
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid archiveId = await CreateFolderAsync(client, "Archive");
        Guid yearId = await CreateFolderAsync(client, "2026", archiveId);
        await CreateFolderAsync(client, "Q1", yearId);
        await CreateFolderAsync(client, "Inbox");

        HttpResponseMessage response = await client.GetAsync($"{FoldersRoute}?view={view}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        FolderNodeDto[] roots = (await response.Content.ReadFromJsonAsync<FolderNodeDto[]>(Ct))!;

        // Name order at every level (03-api-specification.md: deterministic listing).
        roots.Select(r => r.Name).Should().ContainInOrder("Archive", "Inbox");

        FolderNodeDto archive = roots.Single(r => r.Name == "Archive");
        FolderNodeDto year = archive.Children.Should().ContainSingle().Subject;
        year.Name.Should().Be("2026");
        year.ParentId.Should().Be(archiveId);

        FolderNodeDto quarter = year.Children.Should().ContainSingle().Subject;
        quarter.Name.Should().Be("Q1");
        quarter.Children.Should().BeEmpty("tree leaves carry an empty children list");

        roots.Single(r => r.Name == "Inbox").Children.Should().BeEmpty();
    }

    [Fact]
    public async Task ListFolders_WithAnUnknownView_Returns400ViewInvalid()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync($"{FoldersRoute}?view=nested", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_view_invalid");
    }

    [Fact]
    public async Task ListFolders_ReturnsOnlyTheCallersFolders()
    {
        // Owner scoping is structural (05-security.md): another owner's folders
        // are absent from the listing, not forbidden.
        HttpClient first = await AuthenticatedClientAsync();
        await CreateFolderAsync(first, "Private");

        HttpClient second = await AuthenticatedClientAsync();
        await CreateFolderAsync(second, "Mine");

        HttpResponseMessage response = await second.GetAsync(FoldersRoute, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        FolderNodeDto[] items = (await response.Content.ReadFromJsonAsync<FolderNodeDto[]>(Ct))!;
        items.Select(i => i.Name).Should().ContainSingle().Which.Should().Be("Mine");
    }

    [Fact]
    public async Task ListFolders_ExcludesSoftDeletedFolders()
    {
        // No delete endpoint exists yet (#44), so the soft-delete flag is flipped
        // through the module's DbContext — the same database the host queries.
        HttpClient client = await AuthenticatedClientAsync();
        Guid keptId = await CreateFolderAsync(client, "Kept");
        Guid deletedId = await CreateFolderAsync(client, "Deleted");
        await SoftDeleteFolderAsync(deletedId);

        HttpResponseMessage response = await client.GetAsync(FoldersRoute, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        FolderNodeDto[] items = (await response.Content.ReadFromJsonAsync<FolderNodeDto[]>(Ct))!;
        items.Select(i => i.Id).Should().ContainSingle().Which.Should().Be(keptId);
    }

    private async Task SoftDeleteFolderAsync(Guid folderId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoldersDbContext>();
        FolderEntity folder = await db.Folders.SingleAsync(f => f.Id == folderId, Ct);
        folder.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(Ct);
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
        FolderNodeDto created = (await response.Content.ReadFromJsonAsync<FolderNodeDto>(Ct))!;
        return created.Id;
    }

    /// <summary>
    /// The response contract restated independently of the module's DTO, so a
    /// breaking change surfaces as a failing test (12-testing-strategy.md).
    /// <c>Children</c> is null in the flat shape and always present on tree nodes.
    /// </summary>
    private sealed record FolderNodeDto(
        Guid Id,
        Guid? ParentId,
        string Name,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        FolderNodeDto[]? Children);
}
