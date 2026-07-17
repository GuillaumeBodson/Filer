using System.Net;
using System.Net.Http.Json;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Filer.IntegrationTests.Folders;

/// <summary>
/// The rename/move contract end to end against the real host and Postgres
/// (03-api-specification.md): the owner renames and re-parents with merge-patch
/// semantics, cycles are refused with 409 (02-data-model.md), sibling uniqueness
/// holds at the new location, and unowned folders and targets are a uniform 404,
/// never 403 (05-security.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class UpdateFolderEndpointTests(FilerApiFactory factory)
{
    private const string FoldersRoute = "/api/v1/folders";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpdateFolder_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{FoldersRoute}/{Guid.NewGuid()}", new { name = "Renamed" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateFolder_Rename_Returns200AndLeavesTheParentUntouched()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid parentId = await CreateFolderAsync(client, "Parent");
        Guid folderId = await CreateFolderAsync(client, "Old", parentId);

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{FoldersRoute}/{folderId}", new { name = "Renamed" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        FolderDto updated = (await response.Content.ReadFromJsonAsync<FolderDto>(Ct))!;
        updated.Name.Should().Be("Renamed");
        updated.ParentId.Should().Be(parentId, "an absent parentId is merge-patch for 'leave it'");
        updated.UpdatedAt.Should().BeAfter(updated.CreatedAt);
    }

    [Fact]
    public async Task UpdateFolder_MoveUnderAnOwnedParent_Returns200WithTheNewParent()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(client, "Movable");
        Guid targetId = await CreateFolderAsync(client, "Target");

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{FoldersRoute}/{folderId}", new { parentId = targetId }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        FolderDto updated = (await response.Content.ReadFromJsonAsync<FolderDto>(Ct))!;
        updated.ParentId.Should().Be(targetId);
        updated.Name.Should().Be("Movable", "an absent name is merge-patch for 'leave it'");
    }

    [Fact]
    public async Task UpdateFolder_MoveToTheTopLevelWithExplicitNull_Returns200()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid parentId = await CreateFolderAsync(client, "Parent");
        Guid folderId = await CreateFolderAsync(client, "Nested", parentId);

        using var body = new StringContent(
            """{"parentId":null}""", System.Text.Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PatchAsync($"{FoldersRoute}/{folderId}", body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        FolderDto updated = (await response.Content.ReadFromJsonAsync<FolderDto>(Ct))!;
        updated.ParentId.Should().BeNull("explicit null moves to the top level (02-data-model.md)");
    }

    [Fact]
    public async Task UpdateFolder_EmptyPatch_Returns400UpdateEmpty()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(client, "Untouched");

        using var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PatchAsync($"{FoldersRoute}/{folderId}", body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_update_empty");
    }

    [Fact]
    public async Task UpdateFolder_MissingFolder_Returns404FolderNotFound()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{FoldersRoute}/{Guid.NewGuid()}", new { name = "Renamed" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_not_found");
    }

    [Fact]
    public async Task UpdateFolder_AnotherOwnersFolder_Returns404NotForbidden()
    {
        // The uniform-404 rule (05-security.md): a cross-owner folder responds
        // exactly like a missing one, so folder ids cannot be probed.
        HttpClient owner = await AuthenticatedClientAsync();
        Guid foreignId = await CreateFolderAsync(owner, "Private");

        HttpClient other = await AuthenticatedClientAsync();
        HttpResponseMessage response = await other.PatchAsJsonAsync(
            $"{FoldersRoute}/{foreignId}", new { name = "Hijacked" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_not_found");
    }

    [Fact]
    public async Task UpdateFolder_AnotherOwnersTargetParent_Returns404ParentNotFound()
    {
        HttpClient owner = await AuthenticatedClientAsync();
        Guid foreignParentId = await CreateFolderAsync(owner, "Private");

        HttpClient other = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(other, "Movable");

        HttpResponseMessage response = await other.PatchAsJsonAsync(
            $"{FoldersRoute}/{folderId}", new { parentId = foreignParentId }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("parent_folder_not_found");
    }

    [Fact]
    public async Task UpdateFolder_MoveUnderItself_Returns409MoveCycle()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(client, "Loop");

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{FoldersRoute}/{folderId}", new { parentId = folderId }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_move_cycle");
    }

    [Fact]
    public async Task UpdateFolder_MoveUnderItsOwnDescendant_Returns409MoveCycle()
    {
        // Root ─ Child ─ Grandchild: re-parenting Root under Grandchild would make
        // Root its own ancestor (02-data-model.md, cycle prevention).
        HttpClient client = await AuthenticatedClientAsync();
        Guid rootId = await CreateFolderAsync(client, "Root");
        Guid childId = await CreateFolderAsync(client, "Child", rootId);
        Guid grandchildId = await CreateFolderAsync(client, "Grandchild", childId);

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{FoldersRoute}/{rootId}", new { parentId = grandchildId }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_move_cycle");
    }

    [Fact]
    public async Task UpdateFolder_RenameToAnExistingSiblingName_Returns409NameConflict()
    {
        HttpClient client = await AuthenticatedClientAsync();
        await CreateFolderAsync(client, "Taken");
        Guid folderId = await CreateFolderAsync(client, "Old");

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{FoldersRoute}/{folderId}", new { name = "Taken" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_name_conflict");
    }

    [Fact]
    public async Task UpdateFolder_MoveIntoAParentWithACollidingName_Returns409NameConflict()
    {
        // Uniqueness is per (OwnerId, ParentId, Name): the collision appears at the
        // destination, not the origin (02-data-model.md).
        HttpClient client = await AuthenticatedClientAsync();
        Guid targetId = await CreateFolderAsync(client, "Target");
        await CreateFolderAsync(client, "Clash", targetId);
        Guid folderId = await CreateFolderAsync(client, "Clash");

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{FoldersRoute}/{folderId}", new { parentId = targetId }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_name_conflict");
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
