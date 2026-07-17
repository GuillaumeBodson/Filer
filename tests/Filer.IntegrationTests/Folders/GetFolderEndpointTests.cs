using System.Net;
using System.Net.Http.Json;
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
/// The get-folder contract end to end against the real host and Postgres
/// (03-api-specification.md): the owner reads their folder back, and missing,
/// cross-owner, and soft-deleted folders are a uniform 404, never 403
/// (05-security.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class GetFolderEndpointTests(FilerApiFactory factory)
{
    private const string FoldersRoute = "/api/v1/folders";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetFolder_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"{FoldersRoute}/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFolder_OwnedFolder_Returns200WithTheDto()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid parentId = await CreateFolderAsync(client, "Archive");
        Guid folderId = await CreateFolderAsync(client, "2026", parentId);

        HttpResponseMessage response = await client.GetAsync($"{FoldersRoute}/{folderId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        FolderDto folder = (await response.Content.ReadFromJsonAsync<FolderDto>(Ct))!;
        folder.Id.Should().Be(folderId);
        folder.ParentId.Should().Be(parentId);
        folder.Name.Should().Be("2026");
        folder.UpdatedAt.Should().Be(folder.CreatedAt);
    }

    [Fact]
    public async Task GetFolder_MissingId_Returns404FolderNotFound()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"{FoldersRoute}/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_not_found");
    }

    [Fact]
    public async Task GetFolder_AnotherOwnersFolder_Returns404NotForbidden()
    {
        // The uniform-404 rule (05-security.md): a cross-owner folder responds
        // exactly like a missing one, so folder ids cannot be probed.
        HttpClient owner = await AuthenticatedClientAsync();
        Guid foreignId = await CreateFolderAsync(owner, "Private");

        HttpClient other = await AuthenticatedClientAsync();
        HttpResponseMessage response = await other.GetAsync($"{FoldersRoute}/{foreignId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_not_found");
    }

    [Fact]
    public async Task GetFolder_SoftDeletedFolder_Returns404FolderNotFound()
    {
        // No delete endpoint exists yet (#44), so the soft-delete flag is flipped
        // through the module's DbContext — the same database the host queries.
        HttpClient client = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(client, "Doomed");
        await SoftDeleteFolderAsync(folderId);

        HttpResponseMessage response = await client.GetAsync($"{FoldersRoute}/{folderId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("folder_not_found");
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
