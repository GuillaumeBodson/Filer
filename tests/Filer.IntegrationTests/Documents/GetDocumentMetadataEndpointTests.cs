using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The get-metadata contract end to end against the real host and Postgres
/// (03-api-specification.md): the owner reads the document's metadata DTO with no
/// entity leakage, while unknown and soft-deleted documents are a uniform 404 —
/// never 403 (05-security.md). The cross-owner case lives in
/// <see cref="Ownership.OwnershipTests"/>, the dedicated guard for that guarantee
/// (12-testing-strategy.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class GetDocumentMetadataEndpointTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetMetadata_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"{DocumentsRoute}/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMetadata_OwnedDocument_ReturnsTheMetadataDto()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        byte[] bytes = UniquePdfBytes();
        Guid documentId = await UploadAsync(client, bytes, "invoice.pdf");

        HttpResponseMessage response = await client.GetAsync($"{DocumentsRoute}/{documentId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        DocumentMetadata metadata = (await response.Content.ReadFromJsonAsync<DocumentMetadata>(Ct))!;

        metadata.Id.Should().Be(documentId);
        metadata.FolderId.Should().BeNull("uploads land unfiled; the Folders slice is separate (02-data-model.md)");
        metadata.FileName.Should().Be("invoice.pdf");
        metadata.ContentType.Should().Be("application/pdf");
        metadata.SizeBytes.Should().Be(bytes.Length);
        metadata.ContentHash.Should().HaveLength(64, "SHA-256 as lowercase hex (02-data-model.md)");
        metadata.Status.Should().Be("Uploaded");
        metadata.CreatedAt.Should().NotBe(default);
        metadata.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task GetMetadata_ResponseLeaksNoEntityInternals()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        Guid documentId = await UploadAsync(client, UniquePdfBytes());

        HttpResponseMessage response = await client.GetAsync($"{DocumentsRoute}/{documentId}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The DTO boundary (03-api-specification.md, no entity leakage): server-side
        // fields must not appear in the payload under any name/casing.
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        IEnumerable<string> properties = payload.RootElement.EnumerateObject().Select(p => p.Name.ToLowerInvariant());

        properties.Should().NotContain(["ownerid", "tenantid", "storagekey", "deletedat", "metadata"]);
    }

    [Fact]
    public async Task GetMetadata_UnknownDocumentId_Returns404()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        HttpResponseMessage response = await client.GetAsync($"{DocumentsRoute}/{Guid.NewGuid()}", Ct);

        await ShouldBeDocumentNotFoundAsync(response);
    }

    [Fact]
    public async Task GetMetadata_SoftDeletedDocument_Returns404()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        Guid documentId = await UploadAsync(client, UniquePdfBytes());
        await SoftDeleteAsync(client, documentId);

        // Soft-deleted is indistinguishable from never-existed (02-data-model.md).
        HttpResponseMessage response = await client.GetAsync($"{DocumentsRoute}/{documentId}", Ct);

        await ShouldBeDocumentNotFoundAsync(response);
    }

    /// <summary>
    /// Uniform problem-details 404 with the stable error code — identical for
    /// cross-owner, unknown, and soft-deleted so the cases cannot be told apart.
    /// </summary>
    private static async Task ShouldBeDocumentNotFoundAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Code().Should().Be("document_not_found");
    }

    /// <summary>Deletion through the public DELETE endpoint, as a client would (#38).</summary>
    private static async Task SoftDeleteAsync(HttpClient client, Guid documentId)
    {
        HttpResponseMessage response = await client.DeleteAsync($"{DocumentsRoute}/{documentId}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, byte[] bytes, string fileName = "document.pdf")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    /// <summary>Unique per call so tests sharing one database never collide on the dedupe index.</summary>
    private static byte[] UniquePdfBytes() =>
        Encoding.ASCII.GetBytes($"%PDF-1.7 metadata test content {Guid.NewGuid():N}");

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
