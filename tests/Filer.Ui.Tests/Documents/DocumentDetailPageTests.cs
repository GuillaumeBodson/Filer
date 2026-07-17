using Bunit;
using Bunit.TestDoubles;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Documents;
using Filer.Ui.Folders;
using Filer.Ui.Models;
using Filer.Ui.Pages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Ui.Tests.Documents;

public sealed class DocumentDetailPageTests : BunitContext
{
    private static readonly Guid DocId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FolderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakeDocumentsService _service = new();
    private readonly FakeFoldersService _folders = new();

    public DocumentDetailPageTests()
    {
        Services.AddSingleton<IDocumentsService>(_service);
        Services.AddSingleton<IFoldersService>(_folders);
        Services.AddSingleton<Filer.Ui.Tags.ITagsService>(new Filer.Ui.Tests.Tags.FakeTagsService());
    }

    private static DocumentMetadataResult Doc(string name = "invoice.pdf", Guid? folderId = null) =>
        new(new DocumentMetadataResponse
        {
            Id = DocId,
            FileName = name,
            ContentType = "application/pdf",
            SizeBytes = 131072,
            Status = "Ready",
            FolderId = folderId,
            CreatedAt = DateTimeOffset.Now,
        }, null);

    private IRenderedComponent<DocumentDetail> RenderPage() =>
        Render<DocumentDetail>(ps => ps.Add(p => p.Id, DocId));

    [Fact]
    public void Shows_metadata_and_status()
    {
        _service.MetadataResults.Enqueue(Doc(folderId: FolderId));
        _folders.Folders = [new FolderListItemResponse { Id = FolderId, Name = "Taxes" }];

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            cut.Find("h1").TextContent.Should().Be("invoice.pdf");
            cut.Find(".status").ClassList.Should().Contain("status-ok");
            cut.Find(".detail-meta").TextContent.Should()
                .Contain("128 KB").And.Contain("application/pdf").And.Contain("Taxes");
        });
    }

    [Fact]
    public void A_404_renders_the_calm_not_found_view()
    {
        _service.MetadataResults.Enqueue(new DocumentMetadataResult(
            null, ProblemDetailsView.ForStatus(404)));

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[role=alert]").Should().BeEmpty();
            cut.Find(".empty-title").TextContent.Should().Be("Document not found");
        });
    }

    [Fact]
    public void Rename_calls_the_service_and_reloads()
    {
        _service.MetadataResults.Enqueue(Doc());
        _service.NextUpdateResult = new DocumentUpdateResult(
            new UpdateDocumentMetadataResponse { Id = DocId, FileName = "renamed.pdf" }, null);
        _service.MetadataResults.Enqueue(Doc("renamed.pdf"));

        var cut = RenderPage();
        cut.WaitForElement("#rename").Change("renamed.pdf");
        cut.FindAll("form")[0].Submit();

        cut.WaitForAssertion(() =>
        {
            _service.Updates.Should().ContainSingle();
            _service.Updates[0].Update.NewFileName.Should().Be("renamed.pdf");
            _service.Updates[0].Update.MoveFolder.Should().BeFalse();
            cut.Find("h1").TextContent.Should().Be("renamed.pdf");
            cut.Find(".detail-notice").TextContent.Should().Contain("Renamed");
        });
    }

    [Fact]
    public void Move_sends_the_selected_folder()
    {
        _service.MetadataResults.Enqueue(Doc());
        _folders.Folders = [new FolderListItemResponse { Id = FolderId, Name = "Taxes" }];
        _service.NextUpdateResult = new DocumentUpdateResult(
            new UpdateDocumentMetadataResponse { Id = DocId, FileName = "invoice.pdf", FolderId = FolderId }, null);
        _service.MetadataResults.Enqueue(Doc(folderId: FolderId));

        var cut = RenderPage();
        cut.WaitForElement("#move-folder").Change(FolderId.ToString());
        cut.FindAll("form")[1].Submit();

        cut.WaitForAssertion(() =>
        {
            _service.Updates.Should().ContainSingle();
            _service.Updates[0].Update.Should().BeEquivalentTo(
                new DocumentUpdate { MoveFolder = true, TargetFolderId = FolderId });
        });
    }

    [Fact]
    public void Move_to_no_folder_targets_the_root()
    {
        _service.MetadataResults.Enqueue(Doc(folderId: FolderId));
        _folders.Folders = [new FolderListItemResponse { Id = FolderId, Name = "Taxes" }];
        _service.NextUpdateResult = new DocumentUpdateResult(
            new UpdateDocumentMetadataResponse { Id = DocId, FileName = "invoice.pdf" }, null);
        _service.MetadataResults.Enqueue(Doc());

        var cut = RenderPage();
        cut.WaitForElement("#move-folder").Change("");
        cut.FindAll("form")[1].Submit();

        cut.WaitForAssertion(() =>
        {
            _service.Updates.Should().ContainSingle();
            _service.Updates[0].Update.Should().BeEquivalentTo(
                new DocumentUpdate { MoveFolder = true, TargetFolderId = null });
        });
    }

    [Fact]
    public void Download_fetches_bytes_and_hands_them_to_the_browser()
    {
        _service.MetadataResults.Enqueue(Doc());
        _service.NextDownloadResult = new DocumentContentResult("%PDF-1.7"u8.ToArray(), null);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderPage();
        cut.WaitForElement(".detail-download").Click();

        cut.WaitForAssertion(() =>
        {
            _service.Downloads.Should().ContainSingle().Which.Should().Be(DocId);
            JSInterop.VerifyInvoke("filerUi.downloadFile")
                .Arguments.Should().ContainInOrder("invoice.pdf", "application/pdf");
            cut.Find(".detail-notice").TextContent.Should().Contain("Download started");
        });
    }

    [Fact]
    public void Delete_requires_confirmation_then_navigates_back_to_the_list()
    {
        _service.MetadataResults.Enqueue(Doc());

        var cut = RenderPage();
        cut.WaitForElement(".btn-danger").Click();

        _service.Deletes.Should().BeEmpty("the first click only asks for confirmation");
        cut.Find(".detail-confirm").TextContent.Should().Contain("invoice.pdf");

        cut.Find(".detail-confirm-yes").Click();

        cut.WaitForAssertion(() =>
        {
            _service.Deletes.Should().ContainSingle().Which.Should().Be(DocId);
            Services.GetRequiredService<BunitNavigationManager>().Uri.Should().EndWith("/documents");
        });
    }

    [Fact]
    public void Cancelling_the_delete_keeps_the_document()
    {
        _service.MetadataResults.Enqueue(Doc());

        var cut = RenderPage();
        cut.WaitForElement(".btn-danger").Click();
        cut.FindAll(".action-row button")[^1].Click();

        _service.Deletes.Should().BeEmpty();
        cut.FindAll(".detail-confirm").Should().BeEmpty();
    }

    [Fact]
    public void A_failed_action_renders_the_problem_and_stays()
    {
        _service.MetadataResults.Enqueue(Doc());
        _service.NextUpdateResult = new DocumentUpdateResult(null, new ProblemDetailsView
        {
            Title = "Validation failed",
            Detail = "The file name is invalid.",
            Status = 400,
            Code = "file_name_invalid",
        });

        var cut = RenderPage();
        cut.WaitForElement("#rename").Change("///");
        cut.FindAll("form")[0].Submit();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("The file name is invalid."));
    }
}
