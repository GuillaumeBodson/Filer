using System.Net;
using Filer.ApiClient.Generated;
using Filer.Ui.Documents;
using Filer.Ui.Models;
using Filer.Ui.Tests.Auth;
using FluentAssertions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Filer.Ui.Tests.Documents;

/// <summary>
/// Exercises <see cref="DocumentsService"/> through the real Kiota client against a
/// stubbed transport: filter serialization, envelope mapping, and the problem-details
/// contract (#169) on a declared 400.
/// </summary>
public sealed class DocumentsServiceTests
{
    private static DocumentsService CreateService(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.test/"),
        };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient)
        {
            BaseUrl = "https://api.test/",
        };
        return new DocumentsService(new FilerApiClient(adapter));
    }

    [Fact]
    public async Task List_serializes_every_filter_and_maps_the_envelope()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        {
          "items": [
            { "id": "11111111-1111-1111-1111-111111111111", "fileName": "invoice.pdf",
              "contentType": "application/pdf", "sizeBytes": 131072, "status": "Ready",
              "createdAt": "2026-07-01T10:00:00+00:00" }
          ],
          "page": 2, "pageSize": 10, "totalCount": 23, "totalPages": 3
        }
        """);
        DocumentsService service = CreateService(inner);
        var folderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var tagId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        DocumentsPageResult result = await service.ListAsync(
            new DocumentsQuery(folderId, tagId, "tax", Page: 2, PageSize: 10),
            TestContext.Current.CancellationToken);

        result.Problem.Should().BeNull();
        result.Page!.TotalCount.Should().Be(23);
        result.Page.TotalPages.Should().Be(3);
        result.Page.Items.Should().ContainSingle().Which.SizeBytes.Should().Be(131072);

        string query = inner.Requests.Should().ContainSingle().Which.RequestUri!.Query;
        query.Should().Contain($"folderId={folderId}")
            .And.Contain($"tagId={tagId}")
            .And.Contain("q=tax")
            .And.Contain("page=2")
            .And.Contain("pageSize=10");
    }

    [Fact]
    public async Task Blank_text_is_not_sent_as_a_filter()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        { "items": [], "page": 1, "pageSize": 20, "totalCount": 0, "totalPages": 0 }
        """);
        DocumentsService service = CreateService(inner);

        await service.ListAsync(new DocumentsQuery(Text: "   "), TestContext.Current.CancellationToken);

        inner.Requests.Should().ContainSingle().Which.RequestUri!.Query.Should().NotContain("q=");
    }

    [Fact]
    public async Task Upload_posts_multipart_and_maps_the_created_document()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Created, """
        { "id": "11111111-1111-1111-1111-111111111111", "fileName": "invoice.pdf",
          "contentType": "application/pdf", "sizeBytes": 8, "status": "Uploaded",
          "createdAt": "2026-07-17T10:00:00+00:00" }
        """);
        DocumentsService service = CreateService(inner);
        using var content = new MemoryStream("%PDF-1.7"u8.ToArray());

        DocumentUploadResult result = await service.UploadAsync(
            new DocumentUploadRequest(content, "invoice.pdf", "application/pdf"),
            TestContext.Current.CancellationToken);

        result.Problem.Should().BeNull();
        result.Document!.Status.Should().Be("Uploaded");

        var request = inner.Requests.Should().ContainSingle().Which;
        request.RequestUri!.AbsolutePath.Should().Be("/api/v1/documents");
        request.ContentType.Should().StartWith("multipart/form-data");
        System.Text.Encoding.UTF8.GetString(request.Body!).Should()
            .Contain("%PDF-1.7").And.Contain("filename=\"invoice.pdf\"");
    }

    [Fact]
    public async Task Upload_never_reads_the_source_stream_synchronously()
    {
        // Blazor's BrowserFileStream throws on synchronous reads, and Kiota's
        // MultipartBody serializes with a sync CopyTo — the service must buffer.
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Created, """
        { "id": "11111111-1111-1111-1111-111111111111", "fileName": "invoice.pdf",
          "contentType": "application/pdf", "sizeBytes": 8, "status": "Uploaded",
          "createdAt": "2026-07-18T10:00:00+00:00" }
        """);
        DocumentsService service = CreateService(inner);
        using var content = new AsyncOnlyStream("%PDF-1.7"u8.ToArray());

        DocumentUploadResult result = await service.UploadAsync(
            new DocumentUploadRequest(content, "invoice.pdf", "application/pdf"),
            TestContext.Current.CancellationToken);

        result.Problem.Should().BeNull();
        System.Text.Encoding.UTF8.GetString(inner.Requests.Single().Body!).Should().Contain("%PDF-1.7");
    }

    /// <summary>Mimics BrowserFileStream: async reads only, sync reads throw.</summary>
    private sealed class AsyncOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Synchronous reads are not supported.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task The_duplicate_409_carries_the_existing_document_id()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Conflict, """
        {
          "type": "https://docs/errors/duplicate_content",
          "title": "Conflict",
          "status": 409,
          "detail": "A document with identical content already exists.",
          "code": "duplicate_content",
          "existingDocumentId": "22222222-2222-2222-2222-222222222222"
        }
        """);
        DocumentsService service = CreateService(inner);
        using var content = new MemoryStream("%PDF-1.7"u8.ToArray());

        DocumentUploadResult result = await service.UploadAsync(
            new DocumentUploadRequest(content, "copy.pdf", "application/pdf"),
            TestContext.Current.CancellationToken);

        result.Document.Should().BeNull();
        result.Problem!.Code.Should().Be("duplicate_content");
        result.DuplicateOfDocumentId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    [Fact]
    public async Task Metadata_maps_the_document_and_a_404_stays_a_problem()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """
            { "id": "11111111-1111-1111-1111-111111111111", "fileName": "invoice.pdf",
              "contentType": "application/pdf", "sizeBytes": 8, "status": "Ready" }
            """)
            .Enqueue(HttpStatusCode.NotFound, """
            { "type": "https://docs/errors/document_not_found", "title": "Resource not found",
              "status": 404, "detail": "Document not found.", "code": "document_not_found" }
            """);
        DocumentsService service = CreateService(inner);

        DocumentMetadataResult found = await service.GetMetadataAsync(docId, TestContext.Current.CancellationToken);
        DocumentMetadataResult missing = await service.GetMetadataAsync(docId, TestContext.Current.CancellationToken);

        found.Document!.Status.Should().Be("Ready");
        missing.Problem!.IsNotFound.Should().BeTrue();
        inner.Requests.Should().HaveCount(2);
        inner.Requests[0].RequestUri!.AbsolutePath.Should().Be($"/api/v1/documents/{docId}");
    }

    [Fact]
    public async Task Update_patches_only_what_changes()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        { "id": "11111111-1111-1111-1111-111111111111", "fileName": "renamed.pdf",
          "contentType": "application/pdf", "sizeBytes": 8, "status": "Ready" }
        """);
        DocumentsService service = CreateService(inner);

        DocumentUpdateResult result = await service.UpdateAsync(
            docId, new DocumentUpdate { NewFileName = "renamed.pdf" },
            TestContext.Current.CancellationToken);

        result.Document!.FileName.Should().Be("renamed.pdf");
        var request = inner.Requests.Should().ContainSingle().Which;
        request.Method.Method.Should().Be("PATCH");
        string body = System.Text.Encoding.UTF8.GetString(request.Body!);
        body.Should().Contain("\"fileName\"").And.NotContain("folderId");
    }

    [Fact]
    public async Task Moving_to_the_root_sends_an_explicit_null_folderId()
    {
        // Merge-patch (03): absent = untouched, explicit null = root. The generated
        // writer drops null typed properties, so the service goes through
        // AdditionalData - this pins that the null actually reaches the wire.
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        { "id": "11111111-1111-1111-1111-111111111111", "fileName": "invoice.pdf",
          "contentType": "application/pdf", "sizeBytes": 8, "status": "Ready" }
        """);
        DocumentsService service = CreateService(inner);

        await service.UpdateAsync(
            docId, new DocumentUpdate { MoveFolder = true, TargetFolderId = null },
            TestContext.Current.CancellationToken);

        string body = System.Text.Encoding.UTF8.GetString(inner.Requests.Single().Body!);
        body.Replace(" ", "", StringComparison.Ordinal).Should().Contain("\"folderId\":null");
    }

    [Fact]
    public async Task Download_returns_the_raw_bytes()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler().Enqueue(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("%PDF-1.7"u8.ToArray()),
            }));
        DocumentsService service = CreateService(inner);

        DocumentContentResult result = await service.DownloadAsync(docId, TestContext.Current.CancellationToken);

        result.Problem.Should().BeNull();
        System.Text.Encoding.UTF8.GetString(result.Content!).Should().Be("%PDF-1.7");
        inner.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/api/v1/documents/{docId}/content");
    }

    [Fact]
    public async Task Delete_returns_null_on_204_and_the_problem_on_404()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.NoContent)
            .Enqueue(HttpStatusCode.NotFound, """
            { "type": "https://docs/errors/document_not_found", "title": "Resource not found",
              "status": 404, "detail": "Document not found.", "code": "document_not_found" }
            """);
        DocumentsService service = CreateService(inner);

        (await service.DeleteAsync(docId, TestContext.Current.CancellationToken)).Should().BeNull();
        ProblemDetailsView? missing = await service.DeleteAsync(docId, TestContext.Current.CancellationToken);

        missing!.IsNotFound.Should().BeTrue();
        inner.Requests.Should().HaveCount(2);
        inner.Requests[0].Method.Method.Should().Be("DELETE");
    }

    [Fact]
    public async Task A_declared_400_surfaces_the_problem_with_its_code()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.BadRequest, """
        {
          "type": "https://docs/errors/page_size_invalid",
          "title": "Validation failed",
          "status": 400,
          "detail": "The page size must be between 1 and 100.",
          "code": "page_size_invalid"
        }
        """);
        DocumentsService service = CreateService(inner);

        DocumentsPageResult result = await service.ListAsync(
            new DocumentsQuery(PageSize: 999), TestContext.Current.CancellationToken);

        result.Page.Should().BeNull();
        result.Problem!.Code.Should().Be("page_size_invalid");
        result.Problem.Title.Should().Be("Validation failed");
    }

    [Fact]
    public async Task GetAnalysis_maps_the_succeeded_payload_with_its_suggestions()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var folderId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        {
          "documentId": "11111111-1111-1111-1111-111111111111",
          "status": "Succeeded",
          "jobId": "55555555-5555-5555-5555-555555555555",
          "completedAt": "2026-07-18T10:00:00+00:00",
          "suggestions": {
            "suggestedFolder": { "existingFolderId": "44444444-4444-4444-4444-444444444444",
                                 "name": "Factures", "confidence": 0.82 },
            "suggestedTags": [
              { "name": "facture", "confidence": 0.9 },
              { "name": "2026", "confidence": 0.55 }
            ]
          }
        }
        """);
        DocumentsService service = CreateService(inner);

        DocumentAnalysisResult result = await service.GetAnalysisAsync(docId, TestContext.Current.CancellationToken);

        result.Problem.Should().BeNull();
        result.Analysis!.Status.Should().Be("Succeeded");
        result.Analysis.Suggestions!.SuggestedFolder!.Name.Should().Be("Factures");
        result.Analysis.Suggestions.SuggestedFolder.ExistingFolderId.Should().Be(folderId);
        result.Analysis.Suggestions.SuggestedTags.Should().HaveCount(2);
        result.Analysis.Suggestions.SuggestedTags![0].Name.Should().Be("facture");
        result.Analysis.Suggestions.SuggestedTags[0].Confidence.Should().Be(0.9);
        inner.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/api/v1/documents/{docId}/analysis");
    }

    [Fact]
    public async Task GetAnalysis_maps_a_proposed_new_folder_without_an_existing_id()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        {
          "documentId": "11111111-1111-1111-1111-111111111111",
          "status": "Succeeded",
          "jobId": "55555555-5555-5555-5555-555555555555",
          "completedAt": "2026-07-18T10:00:00+00:00",
          "suggestions": {
            "suggestedFolder": { "existingFolderId": null, "name": "Nouveau dossier", "confidence": 0.4 },
            "suggestedTags": []
          }
        }
        """);
        DocumentsService service = CreateService(inner);

        DocumentAnalysisResult result = await service.GetAnalysisAsync(docId, TestContext.Current.CancellationToken);

        result.Analysis!.Suggestions!.SuggestedFolder!.ExistingFolderId.Should().BeNull();
        result.Analysis.Suggestions.SuggestedFolder.Name.Should().Be("Nouveau dossier");
    }

    [Fact]
    public async Task GetAnalysis_maps_a_pending_status_without_suggestions()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        {
          "documentId": "11111111-1111-1111-1111-111111111111",
          "status": "Queued",
          "jobId": "55555555-5555-5555-5555-555555555555",
          "completedAt": null,
          "suggestions": null
        }
        """);
        DocumentsService service = CreateService(inner);

        DocumentAnalysisResult result = await service.GetAnalysisAsync(docId, TestContext.Current.CancellationToken);

        result.Problem.Should().BeNull();
        result.Analysis!.Status.Should().Be("Queued");
        result.Analysis.Suggestions.Should().BeNull();
    }

    [Fact]
    public async Task GetAnalysis_a_404_surfaces_as_not_found()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.NotFound, """
        { "type": "https://docs/errors/document_not_found", "title": "Resource not found",
          "status": 404, "detail": "Document not found.", "code": "document_not_found" }
        """);
        DocumentsService service = CreateService(inner);

        DocumentAnalysisResult result = await service.GetAnalysisAsync(docId, TestContext.Current.CancellationToken);

        result.Analysis.Should().BeNull();
        result.Problem!.IsNotFound.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_posts_the_selection_and_maps_the_applied_tags()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        {
          "documentId": "11111111-1111-1111-1111-111111111111",
          "folderApplied": true,
          "folderId": "44444444-4444-4444-4444-444444444444",
          "tags": [ { "tagId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "source": "AiSuggested" } ]
        }
        """);
        DocumentsService service = CreateService(inner);

        ApplyAnalysisResult result = await service.ApplyAnalysisAsync(
            docId, applyFolder: true, ["facture", "2026"], TestContext.Current.CancellationToken);

        result.Problem.Should().BeNull();
        result.Applied!.FolderApplied.Should().BeTrue();
        result.Applied.Tags.Should().ContainSingle().Which.Source.Should().Be("AiSuggested");

        var request = inner.Requests.Should().ContainSingle().Which;
        request.Method.Method.Should().Be("POST");
        request.RequestUri!.AbsolutePath.Should().Be($"/api/v1/documents/{docId}/analysis/apply");
        string body = System.Text.Encoding.UTF8.GetString(request.Body!).Replace(" ", "", StringComparison.Ordinal);
        body.Should().Contain("\"applyFolder\":true").And.Contain("\"facture\"").And.Contain("\"2026\"");
    }

    [Fact]
    public async Task Apply_sends_an_empty_tags_array_rather_than_omitting_it()
    {
        // A null tags list is a malformed request server-side (analysis_tags_invalid);
        // an empty array legitimately means "no tags" (06).
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        { "documentId": "11111111-1111-1111-1111-111111111111", "folderApplied": true,
          "folderId": "44444444-4444-4444-4444-444444444444", "tags": [] }
        """);
        DocumentsService service = CreateService(inner);

        await service.ApplyAnalysisAsync(docId, applyFolder: true, [], TestContext.Current.CancellationToken);

        string body = System.Text.Encoding.UTF8.GetString(inner.Requests.Single().Body!)
            .Replace(" ", "", StringComparison.Ordinal);
        body.Should().Contain("\"tags\":[]");
    }

    [Fact]
    public async Task Apply_a_declared_400_surfaces_the_stable_code()
    {
        var docId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.BadRequest, """
        {
          "type": "https://docs/errors/suggested_tag_not_created",
          "title": "Validation failed",
          "status": 400,
          "detail": "Create the tag first, then re-apply.",
          "code": "suggested_tag_not_created"
        }
        """);
        DocumentsService service = CreateService(inner);

        ApplyAnalysisResult result = await service.ApplyAnalysisAsync(
            docId, applyFolder: false, ["inexistant"], TestContext.Current.CancellationToken);

        result.Applied.Should().BeNull();
        result.Problem!.Code.Should().Be("suggested_tag_not_created");
    }
}
