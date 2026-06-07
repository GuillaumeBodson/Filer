using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The download contract end to end against the real host, Postgres, and the local
/// storage provider (03-api-specification.md): the owner streams the exact bytes
/// back with the stored content type, while cross-owner, unknown, and soft-deleted
/// documents are a uniform 404 — never 403 (05-security.md) — the ownership
/// integration test 12-testing-strategy.md requires for this slice.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DownloadDocumentContentEndpointTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DownloadContent_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response =
            await client.GetAsync($"{DocumentsRoute}/{Guid.NewGuid()}/content", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DownloadContent_OwnedDocument_StreamsBytesWithStoredContentType()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        byte[] bytes = UniquePdfBytes();
        Guid documentId = await UploadAsync(client, bytes, "invoice.pdf");

        HttpResponseMessage response =
            await client.GetAsync($"{DocumentsRoute}/{documentId}/content", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("invoice.pdf");
        (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(bytes);
    }

    [Fact]
    public async Task DownloadContent_OfAnotherOwnersDocument_Returns404()
    {
        // Owner A uploads…
        HttpClient ownerClient = _factory.CreateClient();
        AuthenticatedUser owner = await ownerClient.RegisterAndAuthenticateAsync();
        ownerClient.WithBearer(owner.AccessToken);
        Guid documentId = await UploadAsync(ownerClient, UniquePdfBytes());

        // …and owner B requests it: 404, never 403 or 200 (05-security.md) — the
        // resource's existence must not be observable across owners.
        HttpClient otherClient = _factory.CreateClient();
        AuthenticatedUser other = await otherClient.RegisterAndAuthenticateAsync();
        otherClient.WithBearer(other.AccessToken);

        HttpResponseMessage response =
            await otherClient.GetAsync($"{DocumentsRoute}/{documentId}/content", Ct);

        await ShouldBeDocumentNotFoundAsync(response);
    }

    [Fact]
    public async Task DownloadContent_UnknownDocumentId_Returns404()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        HttpResponseMessage response =
            await client.GetAsync($"{DocumentsRoute}/{Guid.NewGuid()}/content", Ct);

        await ShouldBeDocumentNotFoundAsync(response);
    }

    [Fact]
    public async Task DownloadContent_SoftDeletedDocument_Returns404()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        Guid documentId = await UploadAsync(client, UniquePdfBytes());
        await SoftDeleteAsync(client, documentId);

        // Soft-deleted is indistinguishable from never-existed (02-data-model.md).
        HttpResponseMessage response =
            await client.GetAsync($"{DocumentsRoute}/{documentId}/content", Ct);

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
        problem.Title.Should().Be("document_not_found");
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
        Encoding.ASCII.GetBytes($"%PDF-1.7 download test content {Guid.NewGuid():N}");

    /// <summary>The slice of the upload response these tests need.</summary>
    private sealed record UploadResult(Guid Id);
}
