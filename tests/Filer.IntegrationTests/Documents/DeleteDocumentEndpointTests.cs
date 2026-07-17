using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.BackgroundJobs.Domain;
using Filer.Modules.BackgroundJobs.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The delete contract end to end against the real host and Postgres
/// (03-api-specification.md): the owner soft-deletes a document and its analysis
/// jobs flip to Cancelled (06-ai-analysis-pipeline.md), after which the document
/// is gone from every read; unknown, already-deleted, and cross-owner documents
/// are a uniform 404, never 403 (05-security.md). The hosted worker is disabled
/// in this test host, so an uploaded document's job stays Queued until the
/// delete cancels it — deterministic, no draining needed.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DeleteDocumentEndpointTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delete_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.DeleteAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_Returns204AndTheDocumentDisappearsFromEveryRead()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client);

        HttpResponseMessage response = await client.DeleteAsync(
            $"{DocumentsRoute}/{documentId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Soft-deleted is indistinguishable from never-existed on every read
        // (02-data-model.md, 05-security.md): metadata, content, and the list.
        (await client.GetAsync($"{DocumentsRoute}/{documentId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{DocumentsRoute}/{documentId}/content", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        DocumentList list = (await client.GetFromJsonAsync<DocumentList>(DocumentsRoute, Ct))!;
        list.Items.Should().NotContain(item => item.Id == documentId);
    }

    [Fact]
    public async Task Delete_CancelsTheDocumentsQueuedAnalysisJob()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client);

        // Upload queued exactly one analysis job; the disabled worker leaves it Queued.
        (await JobStatusesAsync(documentId)).Should().Equal(AnalysisJobStatus.Queued);

        HttpResponseMessage response = await client.DeleteAsync(
            $"{DocumentsRoute}/{documentId}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await JobStatusesAsync(documentId)).Should().Equal(AnalysisJobStatus.Cancelled);
    }

    [Fact]
    public async Task Delete_UnknownDocumentId_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.DeleteAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}", Ct);

        await ShouldBeDocumentNotFoundAsync(response);
    }

    [Fact]
    public async Task Delete_AlreadyDeletedDocument_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(client);

        (await client.DeleteAsync($"{DocumentsRoute}/{documentId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A repeat delete finds nothing: already-deleted is indistinguishable
        // from never-existed (05-security.md).
        HttpResponseMessage second = await client.DeleteAsync(
            $"{DocumentsRoute}/{documentId}", Ct);

        await ShouldBeDocumentNotFoundAsync(second);
    }

    [Fact]
    public async Task Delete_CrossOwner_Returns404AndDeletesNothing()
    {
        // Owner A creates a document…
        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadAsync(owner);

        // …and owner B tries to delete it: uniform 404, not 403 — and definitely
        // no write (05-security.md).
        HttpClient intruder = await AuthenticatedClientAsync();
        HttpResponseMessage response = await intruder.DeleteAsync(
            $"{DocumentsRoute}/{documentId}", Ct);

        await ShouldBeDocumentNotFoundAsync(response);

        // The document is untouched for its owner, and its job is still queued.
        (await owner.GetAsync($"{DocumentsRoute}/{documentId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await JobStatusesAsync(documentId)).Should().Equal(AnalysisJobStatus.Queued);
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

    /// <summary>
    /// The document's job statuses straight from the module-owned <c>jobs</c>
    /// schema — the module maps no job-listing endpoint yet, so the assertion
    /// reads the table the queue writes.
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
        client.WithBearer(user.AccessToken);

        return client;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string fileName = "document.pdf")
    {
        // Unique bytes per call so tests sharing one database never collide on the
        // dedupe index.
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 delete document test content {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    /// <summary>The slice of the upload response these tests need.</summary>
    private sealed record UploadResult(Guid Id);

    /// <summary>The slice of the list response these tests need.</summary>
    private sealed record DocumentList(List<DocumentListItem> Items);

    private sealed record DocumentListItem(Guid Id);
}
