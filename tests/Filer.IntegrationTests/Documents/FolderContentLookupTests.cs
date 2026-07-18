using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.Documents.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The folder-sample read behind the agentic provider (#119) against the real host
/// and Postgres: <see cref="IFolderContentLookup.GetFolderSampleAsync"/> returns
/// the owner's newest file names capped at <c>take</c>, and is owner-scoped by
/// construction — another owner's folder and a soft-deleted folder both read as
/// <b>empty</b>, indistinguishable from an empty folder (the uniform-404 invariant
/// applied to a read, 05-security.md). Security-critical: this is the only data an
/// AI provider can pull mid-analysis (09-decision-log.md note).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class FolderContentLookupTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";
    private const string FoldersRoute = "/api/v1/folders";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetFolderSample_ReturnsTheNewestFileNamesCappedAtTake()
    {
        (HttpClient owner, Guid ownerId) = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(owner, "Invoices");
        await UploadIntoFolderAsync(owner, folderId, "oldest.pdf");
        await UploadIntoFolderAsync(owner, folderId, "middle.pdf");
        await UploadIntoFolderAsync(owner, folderId, "newest.pdf");

        IReadOnlyList<string> sample = await GetFolderSampleAsync(ownerId, folderId, take: 2);

        sample.Should().Equal(["newest.pdf", "middle.pdf"],
            "the sample is the most recently added documents, newest first, capped at take");
    }

    [Fact]
    public async Task GetFolderSample_ForAnotherOwnersFolder_ReturnsEmpty()
    {
        (HttpClient owner, Guid _) = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(owner, "Private");
        await UploadIntoFolderAsync(owner, folderId, "secret.pdf");

        (HttpClient _, Guid otherOwnerId) = await AuthenticatedClientAsync();

        IReadOnlyList<string> sample = await GetFolderSampleAsync(otherOwnerId, folderId, take: 5);

        sample.Should().BeEmpty(
            "a foreign folder reads exactly like an empty one — no other owner's file name may " +
            "ever reach an AI prompt (05, #119)");
    }

    [Fact]
    public async Task GetFolderSample_ForASoftDeletedFolder_ReturnsEmpty()
    {
        (HttpClient owner, Guid ownerId) = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(owner, "Ephemeral");
        await UploadIntoFolderAsync(owner, folderId, "doomed.pdf");

        HttpResponseMessage delete = await owner.DeleteAsync($"{FoldersRoute}/{folderId}?recursive=true", Ct);
        delete.EnsureSuccessStatusCode();

        IReadOnlyList<string> sample = await GetFolderSampleAsync(ownerId, folderId, take: 5);

        sample.Should().BeEmpty(
            "the delete cascade soft-deletes the folder's documents (ADR-007), so a deleted folder " +
            "reads as empty");
    }

    /// <summary>Resolves the lookup exactly as the agentic provider does — through the module's port.</summary>
    private async Task<IReadOnlyList<string>> GetFolderSampleAsync(Guid ownerId, Guid folderId, int take)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        var lookup = scope.ServiceProvider.GetRequiredService<IFolderContentLookup>();

        return await lookup.GetFolderSampleAsync(ownerId, folderId, take, Ct);
    }

    private async Task<(HttpClient Client, Guid OwnerId)> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        return (client, user.Id);
    }

    private static async Task<Guid> CreateFolderAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(FoldersRoute, new { name }, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedFolder>(Ct))!.Id;
    }

    /// <summary>Uploads a small unique document with the given name, then moves it into the folder.</summary>
    private static async Task<Guid> UploadIntoFolderAsync(HttpClient client, Guid folderId, string fileName)
    {
        // Unique bytes per call so tests sharing one database never collide on the
        // dedupe index.
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 folder content lookup test {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();
        Guid documentId = (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;

        HttpResponseMessage move = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{documentId}", new { folderId }, Ct);
        move.EnsureSuccessStatusCode();

        return documentId;
    }

    /// <summary>The slice of the create-folder response these tests need.</summary>
    private sealed record CreatedFolder(Guid Id);
}
