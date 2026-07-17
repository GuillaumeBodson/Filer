using Bunit;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Documents;
using Filer.Ui.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using UploadComponent = Filer.Ui.Components.DocumentUpload;

namespace Filer.Ui.Tests.Documents;

/// <summary>
/// The async-pipeline UX (#136): upload returns immediately, then the status badge
/// transitions Uploaded → Analyzing → Ready by polling; failures render as problems.
/// </summary>
public sealed class DocumentUploadComponentTests : BunitContext
{
    private static readonly Guid DocId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeDocumentsService _service = new();

    public DocumentUploadComponentTests()
    {
        Services.AddSingleton<IDocumentsService>(_service);
    }

    private IRenderedComponent<UploadComponent> RenderUpload(long? maxSizeBytes = null) =>
        Render<UploadComponent>(ps =>
        {
            ps.Add(p => p.PollInterval, TimeSpan.FromMilliseconds(1));
            if (maxSizeBytes is long max)
            {
                ps.Add(p => p.MaxSizeBytes, max);
            }
        });

    private static UploadDocumentResponse Created(string status = "Uploaded") => new()
    {
        Id = DocId,
        FileName = "invoice.pdf",
        Status = status,
        SizeBytes = 1024,
    };

    private static DocumentMetadataResult Meta(string status) =>
        new(new DocumentMetadataResponse { Id = DocId, FileName = "invoice.pdf", Status = status }, null);

    [Fact]
    public void An_oversized_file_is_rejected_client_side_without_a_server_call()
    {
        var cut = RenderUpload(maxSizeBytes: 10);

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("this is more than ten bytes", "big.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("File too large"));
        _service.Uploads.Should().BeEmpty();
    }

    [Fact]
    public void An_unsupported_type_is_rejected_client_side()
    {
        var cut = RenderUpload();

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("MZ...", "virus.exe", contentType: "application/x-msdownload"));

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Unsupported file type"));
        _service.Uploads.Should().BeEmpty();
    }

    [Fact]
    public void A_markdown_file_with_no_browser_type_is_inferred_and_uploaded()
    {
        _service.UploadResult = new DocumentUploadResult(Created("Ready"), null);

        var cut = RenderUpload();
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("# notes", "notes.md", contentType: ""));

        cut.WaitForAssertion(() =>
            _service.Uploads.Should().ContainSingle().Which.ContentType.Should().Be("text/markdown"));
    }

    [Fact]
    public void Upload_lands_then_status_transitions_to_Ready_via_polling()
    {
        _service.UploadResult = new DocumentUploadResult(Created("Uploaded"), null);
        _service.MetadataResults.Enqueue(Meta("Analyzing"));
        _service.MetadataResults.Enqueue(Meta("Ready"));

        var cut = RenderUpload();
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("%PDF-1.7", "invoice.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() =>
        {
            cut.Find(".status").TextContent.Should().Be("Ready");
            cut.Find(".status").ClassList.Should().Contain("status-ok");
        });
        _service.MetadataCalls.Should().HaveCount(2).And.AllBeEquivalentTo(DocId);
    }

    [Fact]
    public void A_failed_analysis_settles_on_the_failed_badge()
    {
        _service.UploadResult = new DocumentUploadResult(Created("Uploaded"), null);
        _service.MetadataResults.Enqueue(Meta("Failed"));

        var cut = RenderUpload();
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("%PDF-1.7", "invoice.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() =>
            cut.Find(".status").ClassList.Should().Contain("status-err"));
    }

    [Fact]
    public void The_duplicate_409_surfaces_the_existing_document_reference()
    {
        var existing = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _service.UploadResult = new DocumentUploadResult(null, new ProblemDetailsView
        {
            Title = "Conflict",
            Detail = "A document with identical content already exists.",
            Status = 409,
            Code = "duplicate_content",
        }, existing);

        var cut = RenderUpload();
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("%PDF-1.7", "copy.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[role=alert]").TextContent.Should().Contain("identical content already exists");
            cut.Find(".upload-duplicate").TextContent.Should().Contain(existing.ToString());
        });
    }

    [Fact]
    public void A_server_413_renders_the_problem()
    {
        _service.UploadResult = new DocumentUploadResult(null, new ProblemDetailsView
        {
            Title = "Payload too large",
            Detail = "The file exceeds the maximum allowed size.",
            Status = 413,
            Code = "file_too_large",
        });

        var cut = RenderUpload();
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("%PDF-1.7", "big.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Payload too large"));
    }

    [Fact]
    public void OnUploaded_fires_after_landing_and_after_settling()
    {
        _service.UploadResult = new DocumentUploadResult(Created("Ready"), null);
        int raised = 0;

        var cut = Render<UploadComponent>(ps => ps
            .Add(p => p.PollInterval, TimeSpan.FromMilliseconds(1))
            .Add(p => p.OnUploaded, () => raised++));

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText("%PDF-1.7", "invoice.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => raised.Should().Be(2));
        _service.MetadataCalls.Should().BeEmpty("a Ready upload needs no polling");
    }
}
