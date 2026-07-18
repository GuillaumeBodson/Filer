using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.BackgroundJobs.Domain;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.Modules.Folders.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.Folders;

/// <summary>
/// The delete-folder contract end to end against the real host and Postgres
/// (ADR-007): an empty folder soft-deletes; a non-empty one is a 409 unless
/// <c>?recursive=true</c>, which cascades over the whole subtree — descendant
/// folders and their documents, one shared <c>DeletedAt</c>, analysis jobs
/// cancelled like a direct document delete (06) — and cross-owner or missing
/// folders are a uniform 404, never 403 (05-security.md). The hosted worker is
/// disabled in this test host, so an uploaded document's job stays Queued until
/// the cascade cancels it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DeleteFolderEndpointTests(FilerApiFactory factory)
{
    private const string FoldersRoute = "/api/v1/folders";
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeleteFolder_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.DeleteAsync(
            $"{FoldersRoute}/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteFolder_EmptyFolder_Returns204AndTheFolderDisappears()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(client, "Disposable");

        HttpResponseMessage response = await client.DeleteAsync($"{FoldersRoute}/{folderId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"{FoldersRoute}/{folderId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A repeat delete finds nothing: already-deleted is indistinguishable
        // from never-existed (05-security.md).
        HttpResponseMessage second = await client.DeleteAsync($"{FoldersRoute}/{folderId}", Ct);
        await ShouldBeProblemAsync(second, HttpStatusCode.NotFound, "folder_not_found");
    }

    [Fact]
    public async Task DeleteFolder_WithAChildFolderAndNoFlag_Returns409AndDeletesNothing()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid parentId = await CreateFolderAsync(client, "Parent");
        Guid childId = await CreateFolderAsync(client, "Child", parentId);

        HttpResponseMessage response = await client.DeleteAsync($"{FoldersRoute}/{parentId}", Ct);

        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "folder_not_empty");

        // ADR-007: the reject makes no change — both folders still readable.
        (await client.GetAsync($"{FoldersRoute}/{parentId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"{FoldersRoute}/{childId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteFolder_WithADocumentInsideAndNoFlag_Returns409AndDeletesNothing()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(client, "WithDocument");
        Guid documentId = await UploadIntoFolderAsync(client, folderId);

        HttpResponseMessage response = await client.DeleteAsync($"{FoldersRoute}/{folderId}", Ct);

        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "folder_not_empty");
        (await client.GetAsync($"{DocumentsRoute}/{documentId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteFolder_Recursive_CascadesFoldersDocumentsAndJobsWithOneTimestamp()
    {
        // Root ─ Child ─ Grandchild with a document in the grandchild, plus an
        // unrelated folder that must survive.
        HttpClient client = await AuthenticatedClientAsync();
        Guid rootId = await CreateFolderAsync(client, "Root");
        Guid childId = await CreateFolderAsync(client, "Child", rootId);
        Guid grandchildId = await CreateFolderAsync(client, "Grandchild", childId);
        Guid survivorId = await CreateFolderAsync(client, "Survivor");
        Guid documentId = await UploadIntoFolderAsync(client, grandchildId);

        (await JobStatusesAsync(documentId)).Should().Equal(AnalysisJobStatus.Queued);

        HttpResponseMessage response = await client.DeleteAsync(
            $"{FoldersRoute}/{rootId}?recursive=true", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The whole subtree and its document are gone from every read…
        foreach (Guid folderId in (Guid[])[rootId, childId, grandchildId])
        {
            (await client.GetAsync($"{FoldersRoute}/{folderId}", Ct))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        (await client.GetAsync($"{DocumentsRoute}/{documentId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // …the document's job is cancelled exactly like a direct delete (06)…
        (await JobStatusesAsync(documentId)).Should().Equal(AnalysisJobStatus.Cancelled);

        // …the unrelated folder survives…
        (await client.GetAsync($"{FoldersRoute}/{survivorId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // …and every row in the cascade carries the same DeletedAt (ADR-007).
        List<DateTimeOffset?> stamps = await FolderDeletedAtAsync(rootId, childId, grandchildId);
        stamps.Should().HaveCount(3);
        stamps.Should().NotContainNulls();
        stamps.Distinct().Should().HaveCount(1, "the cascade shares one timestamp (ADR-007)");
    }

    [Fact]
    public async Task DeleteFolder_RecursiveOnAnEmptyFolder_Returns204()
    {
        // The flag is an opt-in, not a requirement for emptiness: recursive on a
        // leaf works like a plain delete.
        HttpClient client = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(client, "Leaf");

        HttpResponseMessage response = await client.DeleteAsync(
            $"{FoldersRoute}/{folderId}?recursive=true", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteFolder_CrossOwner_Returns404AndDeletesNothing()
    {
        HttpClient owner = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(owner, "Private");

        HttpClient intruder = await AuthenticatedClientAsync();
        HttpResponseMessage response = await intruder.DeleteAsync(
            $"{FoldersRoute}/{folderId}?recursive=true", Ct);

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, "folder_not_found");

        // The folder is untouched for its owner (05-security.md).
        (await owner.GetAsync($"{FoldersRoute}/{folderId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteFolder_MalformedRecursiveValue_Returns400()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(client, "Untouched");

        HttpResponseMessage response = await client.DeleteAsync(
            $"{FoldersRoute}/{folderId}?recursive=banana", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task ShouldBeProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be(code);
    }

    /// <summary>
    /// The subtree's <c>DeletedAt</c> stamps straight from the module-owned
    /// <c>folders</c> schema — the shared-timestamp guarantee is invisible through
    /// the API (deleted folders 404), so the assertion reads the table.
    /// </summary>
    private async Task<List<DateTimeOffset?>> FolderDeletedAtAsync(params Guid[] folderIds)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoldersDbContext>();

        return await db.Folders.AsNoTracking()
            .Where(f => folderIds.Contains(f.Id))
            .Select(f => f.DeletedAt)
            .ToListAsync(Ct);
    }

    /// <summary>
    /// The document's job statuses straight from the module-owned <c>jobs</c>
    /// schema, same as the direct document-delete tests.
    /// </summary>
    private async Task<List<AnalysisJobStatus>> JobStatusesAsync(Guid documentId)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();

        return await db.AnalysisJobs.AsNoTracking()
            .Where(j => j.DocumentId == documentId)
            .Select(j => j.Status)
            .ToListAsync(Ct);
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
        return (await response.Content.ReadFromJsonAsync<FolderDto>(Ct))!.Id;
    }

    /// <summary>Uploads a unique document, then moves it into the folder via PATCH.</summary>
    private static async Task<Guid> UploadIntoFolderAsync(HttpClient client, Guid folderId)
    {
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 delete folder test content {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "document.pdf" } };

        HttpResponseMessage upload = await client.PostAsync(DocumentsRoute, form, Ct);
        upload.EnsureSuccessStatusCode();
        Guid documentId = (await upload.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;

        HttpResponseMessage move = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{documentId}", new { folderId }, Ct);
        move.EnsureSuccessStatusCode();

        return documentId;
    }

    /// <summary>The slice of the folder response these tests need.</summary>
    private sealed record FolderDto(Guid Id);
}
