using Bunit;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Components;
using Filer.Ui.Documents;
using Filer.Ui.Models;
using Filer.Ui.Tags;
using Filer.Ui.Tests.Documents;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Ui.Tests.Tags;

/// <summary>Assignment behavior on the detail page (#139), incl. the Source distinction (02).</summary>
public sealed class DocumentTagsPanelTests : BunitContext
{
    private static readonly Guid DocId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserTag = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AiTag = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FreeTag = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly FakeDocumentsService _documents = new();
    private readonly FakeTagsService _tags = new();

    public DocumentTagsPanelTests()
    {
        Services.AddSingleton<IDocumentsService>(_documents);
        Services.AddSingleton<ITagsService>(_tags);
        _tags.Tags =
        [
            new TagListItemResponse { Id = UserTag, Name = "urgent" },
            new TagListItemResponse { Id = AiTag, Name = "facture" },
            new TagListItemResponse { Id = FreeTag, Name = "perso" },
        ];
    }

    private static DocumentTagsResult Applied(params (Guid TagId, string Source)[] items) =>
        new(new DocumentTagsResponse
        {
            DocumentId = DocId,
            Tags = [.. items.Select(i => new DocumentTagItem { TagId = i.TagId, Source = i.Source })],
        }, null);

    private IRenderedComponent<DocumentTagsPanel> RenderPanel() =>
        Render<DocumentTagsPanel>(ps => ps.Add(p => p.DocumentId, DocId));

    [Fact]
    public void Renders_names_and_distinguishes_ai_suggested_tags()
    {
        _documents.TagsResults.Enqueue(Applied((UserTag, "User"), (AiTag, "AiSuggested")));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            var chips = cut.FindAll(".doc-tag");
            chips.Should().HaveCount(2);
            chips.First(c => c.TextContent.Contains("urgent")).ClassList.Should().NotContain("suggested");
            var suggested = chips.First(c => c.TextContent.Contains("facture"));
            suggested.ClassList.Should().Contain("suggested");
            suggested.QuerySelector(".doc-tag-source")!.TextContent.Should().Be("suggested");
        });
    }

    [Fact]
    public void Adding_a_tag_calls_the_service_and_shows_the_new_set()
    {
        _documents.TagsResults.Enqueue(Applied((UserTag, "User")));
        _documents.TagsResults.Enqueue(Applied((UserTag, "User"), (FreeTag, "User")));

        var cut = RenderPanel();
        cut.WaitForElement("#add-tag").Change(FreeTag.ToString());
        cut.Find(".doc-tags-add").Submit();

        cut.WaitForAssertion(() =>
        {
            _documents.TagAdds.Should().ContainSingle().Which.Should().Be((DocId, FreeTag));
            cut.FindAll(".doc-tag").Should().HaveCount(2);
        });
    }

    [Fact]
    public void The_add_picker_only_offers_unapplied_tags()
    {
        _documents.TagsResults.Enqueue(Applied((UserTag, "User")));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            var options = cut.FindAll("#add-tag option").Select(o => o.TextContent).ToList();
            options.Should().Contain("facture").And.Contain("perso").And.NotContain("urgent");
        });
    }

    [Fact]
    public void Removing_a_tag_calls_the_service_and_drops_the_chip()
    {
        _documents.TagsResults.Enqueue(Applied((UserTag, "User"), (AiTag, "AiSuggested")));

        var cut = RenderPanel();
        cut.WaitForElements(".doc-tag-remove");
        cut.FindAll(".doc-tag-remove")[0].Click();

        cut.WaitForAssertion(() =>
        {
            _documents.TagRemovals.Should().ContainSingle().Which.Should().Be((DocId, UserTag));
            cut.FindAll(".doc-tag").Should().ContainSingle();
        });
    }

    [Fact]
    public void A_cross_owner_tag_renders_the_not_found_problem()
    {
        _documents.TagsResults.Enqueue(Applied((UserTag, "User")));
        _documents.NextRemoveTagResult = ProblemDetailsView.ForStatus(404);

        var cut = RenderPanel();
        cut.WaitForElements(".doc-tag-remove");
        cut.FindAll(".doc-tag-remove")[0].Click();

        cut.WaitForAssertion(() =>
            cut.Find(".empty-title").TextContent.Should().Be("Not found"));
    }
}
