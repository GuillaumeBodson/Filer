using Bunit;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Models;
using Filer.Ui.Pages;
using Filer.Ui.Tags;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Ui.Tests.Tags;

public sealed class TagsPageTests : BunitContext
{
    private static readonly Guid TagId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly FakeTagsService _service = new();

    public TagsPageTests()
    {
        Services.AddSingleton<ITagsService>(_service);
        _service.Tags = [new TagListItemResponse { Id = TagId, Name = "urgent" }];
    }

    private IRenderedComponent<TagsPage> RenderPage() => Render<TagsPage>();

    [Fact]
    public void Lists_tags_with_a_link_into_the_filtered_documents()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tag-name").TextContent.Should().Be("urgent");
            cut.Find($"a[href='documents?tagId={TagId}']").Should().NotBeNull();
        });
    }

    [Fact]
    public void Creates_a_tag_and_reloads()
    {
        _service.NextCreateResult = new TagMutationResult(Guid.NewGuid(), "taxes", null);

        var cut = RenderPage();
        cut.WaitForElement("#new-tag").Change("taxes");
        cut.Find(".tags-create").Submit();

        cut.WaitForAssertion(() =>
        {
            _service.Creates.Should().ContainSingle().Which.Should().Be("taxes");
            cut.Find(".tags-notice").TextContent.Should().Contain("taxes");
        });
    }

    [Fact]
    public void A_duplicate_name_surfaces_the_conflict()
    {
        _service.NextCreateResult = new TagMutationResult(null, null, new ProblemDetailsView
        {
            Title = "Conflict",
            Detail = "A tag with this name already exists.",
            Status = 409,
            Code = "tag_name_conflict",
        });

        var cut = RenderPage();
        cut.WaitForElement("#new-tag").Change("urgent");
        cut.Find(".tags-create").Submit();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("already exists"));
    }

    [Fact]
    public void Rename_flows_through_the_service()
    {
        _service.NextRenameResult = new TagMutationResult(TagId, "very-urgent", null);

        var cut = RenderPage();
        cut.WaitForElements(".tag-action");
        cut.FindAll(".tag-action").First(b => b.TextContent == "Rename").Click();
        cut.Find(".tag-row input").Change("very-urgent");
        cut.Find(".tag-row form").Submit();

        cut.WaitForAssertion(() =>
            _service.Renames.Should().ContainSingle().Which.Should().Be((TagId, "very-urgent")));
    }

    [Fact]
    public void Delete_requires_confirmation()
    {
        var cut = RenderPage();
        cut.WaitForElements(".tag-action");

        cut.FindAll(".tag-action").First(b => b.TextContent == "Delete").Click();
        _service.Deletes.Should().BeEmpty("the first click only asks for confirmation");

        cut.Find(".tags-confirm-yes").Click();

        cut.WaitForAssertion(() =>
            _service.Deletes.Should().ContainSingle().Which.Should().Be(TagId));
    }
}
