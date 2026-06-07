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
/// The upload contract end to end against the real host, Postgres, and the local
/// storage provider (03-api-specification.md, upload behavior): validation (size,
/// allow-list, sniffing), the 409 dedupe path with the existing reference, and the
/// asynchronous 201 with a queued analysis job — required by 12-testing-strategy.md
/// for this slice. Routes and response shapes are restated locally so contract
/// drift fails a test instead of recompiling silently.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class UploadDocumentEndpointTests(FilerApiFactory factory)
{
    private const string UploadRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upload_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(UploadRoute, PdfForm(UniquePdfBytes()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_WithValidPdf_Returns201WithMetadataAndQueuedJob()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        byte[] bytes = UniquePdfBytes();
        HttpResponseMessage response = await client.PostAsync(UploadRoute, PdfForm(bytes, "invoice.pdf"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        UploadResult document = (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!;
        document.Id.Should().NotBeEmpty();
        document.FileName.Should().Be("invoice.pdf");
        document.ContentType.Should().Be("application/pdf");
        document.SizeBytes.Should().Be(bytes.Length);
        document.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
        document.Status.Should().Be("Uploaded");
        // Async contract: the job is queued and referenced immediately; no AI ran inline.
        document.AnalysisJobId.Should().NotBeEmpty();

        response.Headers.Location.Should().Be(new Uri($"{UploadRoute}/{document.Id}", UriKind.Relative));
    }

    [Fact]
    public async Task Upload_SameContentTwiceBySameOwner_Returns409WithExistingReference()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        byte[] bytes = UniquePdfBytes();

        HttpResponseMessage first = await client.PostAsync(UploadRoute, PdfForm(bytes, "original.pdf"), Ct);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        UploadResult original = (await first.Content.ReadFromJsonAsync<UploadResult>(Ct))!;

        // Same bytes under a different name: dedupe is by content hash (02).
        HttpResponseMessage second = await client.PostAsync(UploadRoute, PdfForm(bytes, "copy.pdf"), Ct);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        second.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using JsonDocument problem = JsonDocument.Parse(await second.Content.ReadAsStringAsync(Ct));
        problem.RootElement.GetProperty("title").GetString().Should().Be("duplicate_content");
        problem.RootElement.GetProperty("existingDocumentId").GetString()
            .Should().Be(original.Id.ToString());
    }

    [Fact]
    public async Task Upload_SameContentByDifferentOwner_Returns201()
    {
        byte[] bytes = UniquePdfBytes();

        HttpClient firstClient = _factory.CreateClient();
        AuthenticatedUser firstUser = await firstClient.RegisterAndAuthenticateAsync();
        firstClient.WithBearer(firstUser.AccessToken);
        (await firstClient.PostAsync(UploadRoute, PdfForm(bytes), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Dedupe is owner-scoped (02): another user's identical bytes are not a conflict.
        HttpClient secondClient = _factory.CreateClient();
        AuthenticatedUser secondUser = await secondClient.RegisterAndAuthenticateAsync();
        secondClient.WithBearer(secondUser.AccessToken);

        HttpResponseMessage response = await secondClient.PostAsync(UploadRoute, PdfForm(bytes), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Upload_WithDisallowedContentType_Returns415()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        HttpResponseMessage response = await client.PostAsync(
            UploadRoute, Form([0x50, 0x4B, 0x03, 0x04], "archive.zip", "application/zip"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be("unsupported_file_type");
    }

    [Fact]
    public async Task Upload_WhenMagicBytesContradictDeclaredType_Returns415()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        // Declared as PNG, but the bytes are text: content sniffing must reject (05).
        HttpResponseMessage response = await client.PostAsync(
            UploadRoute, Form(Encoding.UTF8.GetBytes("not an image at all"), "fake.png", "image/png"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be("content_type_mismatch");
    }

    [Fact]
    public async Task Upload_OverConfiguredSizeLimit_Returns413()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        // One byte over the host-under-test ceiling (FilerApiFactory.MaxUploadBytes).
        byte[] oversize = new byte[FilerApiFactory.MaxUploadBytes + 1];
        "%PDF-1.7"u8.ToArray().CopyTo(oversize, 0);

        HttpResponseMessage response = await client.PostAsync(UploadRoute, PdfForm(oversize), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Title.Should().Be("file_too_large");
    }

    [Fact]
    public async Task Upload_WithoutFilePart_Returns400()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("no file here"), "comment");

        HttpResponseMessage response = await client.PostAsync(UploadRoute, form, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Unique per call so tests sharing one database never collide on the dedupe index.</summary>
    private static byte[] UniquePdfBytes() =>
        Encoding.ASCII.GetBytes($"%PDF-1.7 integration test content {Guid.NewGuid():N}");

    private static MultipartFormDataContent PdfForm(byte[] bytes, string fileName = "document.pdf") =>
        Form(bytes, fileName, "application/pdf");

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    /// <summary>Response shape asserted by tests, matching the API's JSON output.</summary>
    private sealed record UploadResult(
        Guid Id, string FileName, string ContentType, long SizeBytes,
        string ContentHash, string Status, DateTimeOffset CreatedAt, Guid AnalysisJobId);
}
