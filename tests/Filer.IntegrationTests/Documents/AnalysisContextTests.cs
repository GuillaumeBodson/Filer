using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.Documents.Contracts;
using Filer.Modules.Folders.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The structural analysis context the worker gathers (#118) against the real host
/// and Postgres: <see cref="IDocumentAnalysisGateway.CountActiveByFolderAsync"/>
/// groups the owner's non-deleted documents per folder, and
/// <see cref="IOwnerFolderReader.ListActiveAsync"/> projects the folder hierarchy.
/// Both reads are owner-scoped by construction, so another owner's organisation —
/// and soft-deleted rows — never enter a provider prompt (the uniform-404
/// invariant applied to context-gathering, 05-security.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AnalysisContextTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";
    private const string FoldersRoute = "/api/v1/folders";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CountActiveByFolder_GroupsPerFolderAndNeverSeesAnotherOwner()
    {
        (HttpClient owner, Guid ownerId) = await AuthenticatedClientAsync();
        Guid invoices = await CreateFolderAsync(owner, "Invoices");
        Guid empty = await CreateFolderAsync(owner, "Empty");
        await UploadIntoFolderAsync(owner, invoices);
        await UploadIntoFolderAsync(owner, invoices);
        await UploadAsync(owner); // unfiled — belongs to no folder's count

        (HttpClient otherOwner, Guid otherOwnerId) = await AuthenticatedClientAsync();
        Guid foreign = await CreateFolderAsync(otherOwner, "Private");
        await UploadIntoFolderAsync(otherOwner, foreign);

        IReadOnlyDictionary<Guid, int> ownerCounts = await CountActiveByFolderAsync(ownerId);
        IReadOnlyDictionary<Guid, int> otherCounts = await CountActiveByFolderAsync(otherOwnerId);

        ownerCounts.Should().Equal(new Dictionary<Guid, int> { [invoices] = 2 },
            "counts group per folder, empty folders ({0}) are absent, and another owner's documents never appear",
            empty);
        otherCounts.Should().Equal(new Dictionary<Guid, int> { [foreign] = 1 });
    }

    [Fact]
    public async Task CountActiveByFolder_ExcludesSoftDeletedDocuments()
    {
        (HttpClient owner, Guid ownerId) = await AuthenticatedClientAsync();
        Guid folderId = await CreateFolderAsync(owner, "Taxes");
        await UploadIntoFolderAsync(owner, folderId);
        Guid deletedId = await UploadIntoFolderAsync(owner, folderId);

        HttpResponseMessage delete = await owner.DeleteAsync($"{DocumentsRoute}/{deletedId}", Ct);
        delete.EnsureSuccessStatusCode();

        IReadOnlyDictionary<Guid, int> counts = await CountActiveByFolderAsync(ownerId);

        counts.Should().Equal(new Dictionary<Guid, int> { [folderId] = 1 },
            "a soft-deleted document leaves the count immediately");
    }

    [Fact]
    public async Task OwnerFolderReader_ProjectsParentIdAndNeverSeesAnotherOwner()
    {
        (HttpClient owner, Guid ownerId) = await AuthenticatedClientAsync();
        Guid parentId = await CreateFolderAsync(owner, "Archive");
        Guid childId = await CreateFolderAsync(owner, "2026", parentId);

        (HttpClient otherOwner, Guid _) = await AuthenticatedClientAsync();
        Guid foreignId = await CreateFolderAsync(otherOwner, "Private");

        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IOwnerFolderReader>();
        IReadOnlyList<OwnerFolder> folders = await reader.ListActiveAsync(ownerId, Ct);

        // Name-then-id ordering puts "2026" first; the child carries its parent's id.
        folders.Should().Equal(
            new OwnerFolder(childId, "2026", parentId),
            new OwnerFolder(parentId, "Archive", ParentId: null));
        folders.Select(f => f.Id).Should().NotContain(foreignId,
            "another owner's folders never enter the analysis context (05)");
    }

    /// <summary>Resolves the count seam exactly as the worker does — through the gateway.</summary>
    private async Task<IReadOnlyDictionary<Guid, int>> CountActiveByFolderAsync(Guid ownerId)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IDocumentAnalysisGateway>();

        return await gateway.CountActiveByFolderAsync(ownerId, Ct);
    }

    private async Task<(HttpClient Client, Guid OwnerId)> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        return (client, user.Id);
    }

    private static async Task<Guid> CreateFolderAsync(HttpClient client, string name, Guid? parentId = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            FoldersRoute, new { name, parentId }, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedFolder>(Ct))!.Id;
    }

    /// <summary>Uploads a small unique document, then moves it into the folder.</summary>
    private static async Task<Guid> UploadIntoFolderAsync(HttpClient client, Guid folderId)
    {
        Guid documentId = await UploadAsync(client);

        HttpResponseMessage move = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{documentId}", new { folderId }, Ct);
        move.EnsureSuccessStatusCode();

        return documentId;
    }

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        // Unique bytes per call so tests sharing one database never collide on the
        // dedupe index.
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 analysis context test content {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "context.pdf" } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    /// <summary>The slice of the upload response these tests need.</summary>
    private sealed record UploadResult(Guid Id);

    /// <summary>The slice of the create-folder response these tests need.</summary>
    private sealed record CreatedFolder(Guid Id);
}
