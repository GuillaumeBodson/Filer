using Bunit;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Folders;
using Filer.Ui.Models;
using Filer.Ui.Pages;
using Filer.Ui.Tests.Documents;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Ui.Tests.Folders;

public sealed class FoldersPageTests : BunitContext
{
    private static readonly Guid Root = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Child = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Other = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly FakeFoldersService _service = new();

    public FoldersPageTests()
    {
        Services.AddSingleton<IFoldersService>(_service);
        _service.Folders =
        [
            new FolderListItemResponse { Id = Root, Name = "Taxes" },
            new FolderListItemResponse { Id = Child, Name = "2026", ParentId = Root },
            new FolderListItemResponse { Id = Other, Name = "Photos" },
        ];
    }

    private IRenderedComponent<FoldersPage> RenderPage() => Render<FoldersPage>();

    [Fact]
    public void Renders_the_tree_with_nesting()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[role=tree]").TextContent.Should().Contain("Taxes").And.Contain("Photos");
            // "2026" sits inside Taxes' nested group.
            cut.Find("[role=tree] [role=group]").TextContent.Should().Contain("2026");
        });
    }

    [Fact]
    public void Creates_a_root_folder_and_reloads()
    {
        _service.NextCreateResult = new FolderCreateResult(
            new CreateFolderResponse { Id = Guid.NewGuid(), Name = "Contracts" }, null);

        var cut = RenderPage();
        cut.WaitForElement("#new-folder").Change("Contracts");
        cut.FindAll("form")[0].Submit();

        cut.WaitForAssertion(() =>
        {
            _service.Creates.Should().ContainSingle().Which.Should().Be(("Contracts", (Guid?)null));
            cut.Find(".action-notice").TextContent.Should().Contain("Contracts");
        });
    }

    [Fact]
    public void Selecting_a_folder_offers_subfolder_rename_move_delete()
    {
        var cut = RenderPage();
        cut.WaitForElements(".folder-name");

        cut.FindAll(".folder-name").First(b => b.TextContent == "Taxes").Click();

        cut.Find(".folder-actions h2").TextContent.Should().Be("Taxes");
        cut.Find("#rename-folder").GetAttribute("value").Should().Be("Taxes");
        cut.Find("a[href='documents?folderId=" + Root + "']").Should().NotBeNull();
    }

    [Fact]
    public void The_move_targets_exclude_the_folder_and_its_descendants()
    {
        var cut = RenderPage();
        cut.WaitForElements(".folder-name");
        cut.FindAll(".folder-name").First(b => b.TextContent == "Taxes").Click();

        var options = cut.FindAll("#move-parent option").Select(o => o.TextContent).ToList();

        options.Should().Contain("(Root)").And.Contain("Photos");
        options.Should().NotContain("Taxes", "a folder can't move under itself")
            .And.NotContain("2026", "a folder can't move into its own subtree");
    }

    [Fact]
    public void A_cycle_rejected_by_the_server_renders_clear_feedback()
    {
        _service.NextUpdateResult = new FolderUpdateResult(null, new ProblemDetailsView
        {
            Title = "Conflict",
            Detail = "Moving the folder there would create a cycle.",
            Status = 409,
            Code = "folder_move_cycle",
        });

        var cut = RenderPage();
        cut.WaitForElements(".folder-name");
        cut.FindAll(".folder-name").First(b => b.TextContent == "Taxes").Click();
        cut.Find("#move-parent").Change(Other.ToString());
        cut.FindAll("form").Last(f => f.QuerySelector("#move-parent") is not null).Submit();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("would create a cycle"));
    }

    [Fact]
    public void Deleting_a_non_empty_folder_without_cascade_shows_the_rejection()
    {
        _service.NextDeleteResult = new ProblemDetailsView
        {
            Title = "Conflict",
            Detail = "The folder is not empty. Delete its contents first or opt into a recursive delete.",
            Status = 409,
            Code = "folder_not_empty",
        };

        var cut = RenderPage();
        cut.WaitForElements(".folder-name");
        cut.FindAll(".folder-name").First(b => b.TextContent == "Taxes").Click();
        cut.Find(".btn-danger").Click();
        cut.Find(".folders-confirm-yes").Click();

        cut.WaitForAssertion(() =>
        {
            _service.Deletes.Should().ContainSingle().Which.Should().Be((Root, false));
            cut.Find("[role=alert]").TextContent.Should().Contain("not empty");
        });
    }

    [Fact]
    public void The_cascade_checkbox_opts_into_a_recursive_delete()
    {
        var cut = RenderPage();
        cut.WaitForElements(".folder-name");
        cut.FindAll(".folder-name").First(b => b.TextContent == "Taxes").Click();
        cut.Find(".btn-danger").Click();
        cut.Find(".folders-cascade input").Change(true);
        cut.Find(".folders-confirm-yes").Click();

        cut.WaitForAssertion(() =>
            _service.Deletes.Should().ContainSingle().Which.Should().Be((Root, true)));
    }

    [Fact]
    public void A_failed_load_offers_retry()
    {
        _service.Problem = ProblemDetailsView.ForStatus(500, "Server error");

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Find(".error-retry").Should().NotBeNull());
    }
}
