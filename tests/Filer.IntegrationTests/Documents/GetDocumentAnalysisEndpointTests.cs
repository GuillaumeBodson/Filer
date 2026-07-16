using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The analysis-status contract end to end against the real host and Postgres
/// (#54, 03-api-specification.md): an upload queues a real job, the owner reads
/// its status and — once succeeded — the suggestions; a terminal failure surfaces
/// as a bare "Failed" with no provider detail, and a missing or cross-owner
/// document is a uniform 404, never 403 (05-security.md) — exercised through the
/// real pipeline: JWT validation -> ICurrentUser -> owner-scoped lookup ->
/// cross-module job read -> problem-details.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class GetDocumentAnalysisEndpointTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";

    private static readonly System.Text.Json.JsonSerializerOptions WebJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetAnalysis_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/analysis", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAnalysis_AfterUpload_ReturnsQueuedWithoutSuggestions()
    {
        // The worker is disabled in the test host, so the job the upload queued
        // stays Queued — exactly the pending shape the client polls against.
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);

        HttpResponseMessage response = await client.GetAsync(
            $"{DocumentsRoute}/{documentId}/analysis", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AnalysisDto result = (await response.Content.ReadFromJsonAsync<AnalysisDto>(Ct))!;
        result.DocumentId.Should().Be(documentId);
        result.Status.Should().Be("Queued");
        result.JobId.Should().NotBeNull();
        result.Suggestions.Should().BeNull();
    }

    [Fact]
    public async Task GetAnalysis_WhenSucceeded_ReturnsSuggestions()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);

        // The wire shape the worker (#53) writes: Web defaults — camelCase —
        // restated here independently so drift fails a test. The payload keeps the
        // legacy duplicateSignals field (dropped in #164): rows persisted before
        // the removal must still be readable.
        Guid jobId = await _factory.CompleteLatestAnalysisJobAsync(documentId, """
            {
              "suggestedFolder": { "existingFolderId": null, "name": "Invoices", "confidence": 0.92 },
              "suggestedTags": [ { "name": "invoices", "confidence": 0.81 } ],
              "duplicateSignals": []
            }
            """);

        HttpResponseMessage response = await client.GetAsync(
            $"{DocumentsRoute}/{documentId}/analysis", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().NotContain(
            "duplicateSignals", "the removed field must not resurface on the response (#164)");

        AnalysisDto result = System.Text.Json.JsonSerializer.Deserialize<AnalysisDto>(body, WebJsonOptions)!;
        result.Status.Should().Be("Succeeded");
        result.JobId.Should().Be(jobId);
        result.Suggestions.Should().NotBeNull();
        result.Suggestions!.SuggestedFolder!.Name.Should().Be("Invoices");
        result.Suggestions.SuggestedFolder.ExistingFolderId.Should().BeNull();
        result.Suggestions.SuggestedTags.Should().ContainSingle()
            .Which.Name.Should().Be("invoices");
    }

    [Fact]
    public async Task GetAnalysis_WhenFailedTerminally_SurfacesUnavailable_WithoutErrorDetail()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);

        const string internalError = "provider exploded with secret detail";
        await _factory.FailLatestAnalysisJobAsync(documentId, internalError);

        HttpResponseMessage response = await client.GetAsync(
            $"{DocumentsRoute}/{documentId}/analysis", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().NotContain(internalError, "provider failure detail must never reach clients (05-security.md)");

        AnalysisDto result = System.Text.Json.JsonSerializer.Deserialize<AnalysisDto>(body, WebJsonOptions)!;
        result.Status.Should().Be("Failed");
        result.Suggestions.Should().BeNull();
    }

    [Fact]
    public async Task GetAnalysis_DocumentOfAnotherOwner_Returns404()
    {
        // The required security test: owner A's analysis must be invisible to
        // owner B — not 403 (which would confirm the document exists).
        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(owner);

        HttpClient intruder = await AuthenticatedClientAsync();

        HttpResponseMessage response = await intruder.GetAsync(
            $"{DocumentsRoute}/{documentId}/analysis", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAnalysis_UnknownDocument_Returns404()
    {
        HttpClient client = await AuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/analysis", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        return client.WithBearer(user.AccessToken);
    }

    private static async Task<Guid> UploadDocumentAsync(HttpClient client)
    {
        var file = new ByteArrayContent(
            Encoding.ASCII.GetBytes($"%PDF-1.7 analysis-status {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "analysis.pdf" } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    /// <summary>The slices of the contracts these tests need, restated independently.</summary>
    private sealed record UploadResult(Guid Id);

    private sealed record AnalysisDto(
        Guid DocumentId, string Status, Guid? JobId, DateTimeOffset? CompletedAt, SuggestionsDto? Suggestions);

    private sealed record SuggestionsDto(
        FolderSuggestionDto? SuggestedFolder,
        IReadOnlyList<TagSuggestionDto> SuggestedTags);

    private sealed record FolderSuggestionDto(Guid? ExistingFolderId, string Name, double Confidence);

    private sealed record TagSuggestionDto(string Name, double Confidence);
}
