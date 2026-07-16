using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Filer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Filer.IntegrationTests.Documents;

/// <summary>
/// The apply-suggestions contract end to end against the real host and Postgres
/// (#55, 06-ai-analysis-pipeline.md): the owner confirms a subset of a succeeded
/// analysis — the folder moves and the confirmed tags become AiSuggested
/// association rows — while a document with no completed analysis, and any missing
/// or cross-owner document, is a uniform 404, never 403 (05-security.md) —
/// exercised through the real pipeline: JWT validation -> ICurrentUser ->
/// owner-scoped lookup -> cross-module job read + tag resolution -> one-transaction
/// write -> problem-details.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ApplyDocumentAnalysisEndpointTests(FilerApiFactory factory)
{
    private const string DocumentsRoute = "/api/v1/documents";
    private const string FoldersRoute = "/api/v1/folders";
    private const string TagsRoute = "/api/v1/tags";

    private static readonly string[] NeverSuggestedTags = ["never-suggested"];

    private readonly FilerApiFactory _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Apply_WithoutBearerToken_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{DocumentsRoute}/{Guid.NewGuid()}/analysis/apply",
            new { applyFolder = false, tags = Array.Empty<string>() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Apply_ConfirmedFolderAndTag_MovesDocumentAndCreatesAiSuggestedRow()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);
        Guid folderId = await CreateFolderAsync(client, $"Invoices-{Guid.NewGuid():N}");
        string tagName = $"invoices-{Guid.NewGuid():N}";
        Guid tagId = await CreateTagAsync(client, tagName);

        await _factory.CompleteLatestAnalysisJobAsync(documentId, $$"""
            {
              "suggestedFolder": { "existingFolderId": "{{folderId}}", "name": "Invoices", "confidence": 0.92 },
              "suggestedTags": [ { "name": "{{tagName}}", "confidence": 0.81 } ]
            }
            """);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/analysis/apply",
            new { applyFolder = true, tags = new[] { tagName } }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ApplyResultDto result = (await response.Content.ReadFromJsonAsync<ApplyResultDto>(Ct))!;
        result.DocumentId.Should().Be(documentId);
        result.FolderApplied.Should().BeTrue();
        result.FolderId.Should().Be(folderId);
        result.Tags.Should().ContainSingle(t => t.TagId == tagId)
            .Which.Source.Should().Be("AiSuggested");

        // The move really persisted: the metadata read reflects the new folder.
        DocumentMetadataDto metadata = (await client.GetFromJsonAsync<DocumentMetadataDto>(
            $"{DocumentsRoute}/{documentId}", Ct))!;
        metadata.FolderId.Should().Be(folderId);
    }

    [Fact]
    public async Task Apply_AcceptingNone_SucceedsWithoutChanges()
    {
        // "A user may accept all, some, or none" (06): none is a 200, not an error.
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);

        await _factory.CompleteLatestAnalysisJobAsync(documentId, """
            {
              "suggestedFolder": null,
              "suggestedTags": [ { "name": "anything", "confidence": 0.5 } ]
            }
            """);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/analysis/apply",
            new { applyFolder = false, tags = Array.Empty<string>() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ApplyResultDto result = (await response.Content.ReadFromJsonAsync<ApplyResultDto>(Ct))!;
        result.FolderApplied.Should().BeFalse();
        result.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_TagNotAmongSuggestions_Returns400()
    {
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);

        await _factory.CompleteLatestAnalysisJobAsync(documentId, """
            { "suggestedFolder": null, "suggestedTags": [] }
            """);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/analysis/apply",
            new { applyFolder = false, tags = NeverSuggestedTags }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Apply_WithoutCompletedAnalysis_Returns404()
    {
        // The upload queued a job but the worker is disabled, so it is still
        // Queued: there is nothing to apply yet.
        HttpClient client = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/analysis/apply",
            new { applyFolder = false, tags = Array.Empty<string>() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Apply_DocumentOfAnotherOwner_Returns404()
    {
        // The required security test: owner A's document must be invisible to
        // owner B — not 403 (which would confirm it exists), not an apply.
        HttpClient owner = await AuthenticatedClientAsync();
        Guid documentId = await UploadDocumentAsync(owner);
        await _factory.CompleteLatestAnalysisJobAsync(documentId, """
            { "suggestedFolder": null, "suggestedTags": [] }
            """);

        HttpClient intruder = await AuthenticatedClientAsync();

        HttpResponseMessage response = await intruder.PostAsJsonAsync(
            $"{DocumentsRoute}/{documentId}/analysis/apply",
            new { applyFolder = false, tags = Array.Empty<string>() }, Ct);

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
            Encoding.ASCII.GetBytes($"%PDF-1.7 analysis-apply {Guid.NewGuid():N}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "apply.pdf" } };

        HttpResponseMessage response = await client.PostAsync(DocumentsRoute, form, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadResult>(Ct))!.Id;
    }

    private static async Task<Guid> CreateFolderAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(FoldersRoute, new { name }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FolderDto>(Ct))!.Id;
    }

    private static async Task<Guid> CreateTagAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(TagsRoute, new { name }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TagDto>(Ct))!.Id;
    }

    /// <summary>The slices of the contracts these tests need, restated independently.</summary>
    private sealed record UploadResult(Guid Id);

    private sealed record FolderDto(Guid Id);

    private sealed record TagDto(Guid Id, string Name);

    private sealed record ApplyResultDto(
        Guid DocumentId, bool FolderApplied, Guid? FolderId, IReadOnlyList<TagItemDto> Tags);

    private sealed record TagItemDto(Guid TagId, string Source);

    private sealed record DocumentMetadataDto(Guid Id, Guid? FolderId);
}
