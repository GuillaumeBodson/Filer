using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The update-metadata contract end to end against the real host and Postgres
/// (03-api-specification.md): the owner renames and moves a document with
/// merge-patch semantics, while unknown, soft-deleted, and cross-owner documents
/// — and unowned move targets — are a uniform 404, never 403 (05-security.md).
/// Until the Folders module lands (M4, #40–#44) no folder exists, so every
/// non-null move target is the unowned case by definition.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class UpdateDocumentMetadataEndpointTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpdateMetadata_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}", new { fileName = "renamed.pdf" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateMetadata_Rename_PersistsAndReturnsTheUpdatedDto()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client, "original.pdf");

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{documentId}", new { fileName = "renamed.pdf" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DocumentMetadata updated = (await response.Content.ReadFromJsonAsync<DocumentMetadata>(Ct))!;
        updated.Id.Should().Be(documentId);
        updated.FileName.Should().Be("renamed.pdf");
        updated.FolderId.Should().BeNull("an absent folderId leaves the folder untouched");
        updated.UpdatedAt.Should().BeOnOrAfter(updated.CreatedAt);

        // The rename is durable, not just echoed: a subsequent read agrees.
        DocumentMetadata reread = (await client.GetFromJsonAsync<DocumentMetadata>(
            $"{DocumentsRoute}/{documentId}", Ct))!;
        reread.FileName.Should().Be("renamed.pdf");
    }

    [Fact]
    public async Task UpdateMetadata_MoveToUnownedFolder_Returns404FolderNotFound()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client);

        // No folders exist before M4, so any non-null target is unowned — the
        // same uniform 404 a cross-owner folder will produce once they do.
        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{documentId}", new { folderId = Guid.NewGuid() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be("folder_not_found");
    }

    [Fact]
    public async Task UpdateMetadata_ExplicitNullFolderId_MovesToRootAndReturns200()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client);

        // Merge-patch semantics: "folderId": null is a move to root, not "absent".
        using var body = new StringContent("""{"folderId":null}""", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PatchAsync($"{DocumentsRoute}/{documentId}", body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DocumentMetadata updated = (await response.Content.ReadFromJsonAsync<DocumentMetadata>(Ct))!;
        updated.FolderId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateMetadata_EmptyBody_Returns400UpdateEmpty()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client);

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{documentId}", new { }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be("update_empty");
    }

    [Theory]
    [InlineData("""{"fileName":null}""")]
    [InlineData("""{"fileName":""}""")]
    [InlineData("""{"fileName":"   "}""")]
    public async Task UpdateMetadata_InvalidFileName_Returns400FileNameInvalid(string json)
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client);

        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PatchAsync($"{DocumentsRoute}/{documentId}", body, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be("file_name_invalid");
    }

    [Fact]
    public async Task UpdateMetadata_UnknownDocumentId_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}", new { fileName = "renamed.pdf" }, Ct);

        await ShouldBeDocumentNotFoundAsync(response);
    }

    [Fact]
    public async Task UpdateMetadata_SoftDeletedDocument_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client);
        await SoftDeleteAsync(documentId);

        // Soft-deleted is indistinguishable from never-existed (02-data-model.md).
        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{documentId}", new { fileName = "renamed.pdf" }, Ct);

        await ShouldBeDocumentNotFoundAsync(response);
    }

    [Fact]
    public async Task UpdateMetadata_CrossOwner_Returns404AndChangesNothing()
    {
        // Owner A creates a document…
        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(owner, "owned.pdf");

        // …and owner B tries to rename it. A mutation makes the uniform-404 rule
        // bite hardest: not 403, and definitely no write (05-security.md).
        HttpClient intruder = await AuthenticatedClientAsync();
        HttpResponseMessage response = await intruder.PatchAsJsonAsync(
            $"{DocumentsRoute}/{documentId}", new { fileName = "hijacked.pdf" }, Ct);

        await ShouldBeDocumentNotFoundAsync(response);

        DocumentMetadata unchanged = (await owner.GetFromJsonAsync<DocumentMetadata>(
            $"{DocumentsRoute}/{documentId}", Ct))!;
        unchanged.FileName.Should().Be("owned.pdf");
    }

    [Fact]
    public async Task UpdateMetadata_ResponseLeaksNoEntityInternals()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client);

        HttpResponseMessage response = await client.PatchAsJsonAsync(
            $"{DocumentsRoute}/{documentId}", new { fileName = "renamed.pdf" }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The DTO boundary (03-api-specification.md, no entity leakage): server-side
        // fields must not appear in the payload under any name/casing.
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        IEnumerable<string> properties = payload.RootElement.EnumerateObject().Select(p => p.Name.ToLowerInvariant());

        properties.Should().NotContain(["ownerid", "tenantid", "storagekey", "deletedat", "metadata"]);
    }

    /// <summary>
    /// Uniform problem-details 404 with the stable error code — identical for
    /// cross-owner, unknown, and soft-deleted so the cases cannot be told apart.
    /// </summary>
    private static async Task ShouldBeDocumentNotFoundAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be("document_not_found");
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        return client;
    }

    /// <summary>
    /// No DELETE endpoint exists yet, so deletion is arranged through the module's
    /// DbContext. Replace with the API call once the delete slice lands (03).
    /// </summary>
    private async Task SoftDeleteAsync(Guid documentId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        Document document = await db.Documents.SingleAsync(d => d.Id == documentId, Ct);
        document.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string fileName = "document.pdf")
    {
        // Unique bytes per call so tests sharing one database never collide on the
        // dedupe index.
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 update metadata test content {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    /// <summary>The metadata contract, restated independently of the module's DTO (12-testing-strategy.md).</summary>
    private sealed record DocumentMetadata(
        Guid Id,
        Guid? FolderId,
        string FileName,
        string ContentType,
        long SizeBytes,
        string ContentHash,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    /// <summary>The slice of the upload response these tests need.</summary>
    private sealed record UploadResult(Guid Id);
}
