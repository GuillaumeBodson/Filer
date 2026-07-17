using Bunit;
using Bunit.TestDoubles;
using Filer.ApiClient.Generated.Models;
using Filer.Ui.Documents;
using Filer.Ui.Models;
using Filer.Ui.Tests.Documents;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DocumentsPage = Filer.Ui.Pages.Documents;

namespace Filer.Ui.Tests.Documents;

public sealed class DocumentsPageTests : BunitContext
{
    private readonly FakeDocumentsService _service = new();

    public DocumentsPageTests()
    {
        Services.AddSingleton<IDocumentsService>(_service);
    }

    private static DocumentsPageResult PageOf(
        int page, int pageSize, long totalCount, int totalPages, params string[] fileNames) =>
        new(new PagedResultOfDocumentListItemResponse
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = [.. fileNames.Select(name => new DocumentListItemResponse
            {
                Id = Guid.NewGuid(),
                FileName = name,
                SizeBytes = 128 * 1024,
                Status = "Ready",
                ContentType = "application/pdf",
                CreatedAt = DateTimeOffset.Now,
            })],
        }, null);

    private IRenderedComponent<DocumentsPage> RenderAt(string relativeUri)
    {
        Services.GetRequiredService<BunitNavigationManager>().NavigateTo(relativeUri);
        return Render<DocumentsPage>();
    }

    [Fact]
    public void Renders_rows_bound_to_the_paged_envelope()
    {
        _service.Default = PageOf(1, 20, 2, 1, "invoice.pdf", "lease.pdf");

        var cut = RenderAt("documents");

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("tbody tr").Count.Should().Be(2);
            cut.Find("tbody").TextContent.Should().Contain("invoice.pdf").And.Contain("128 KB");
            cut.FindAll(".status-ok").Should().NotBeEmpty();
            cut.Find(".pager-status").TextContent.Should().Contain("Page 1 of 1").And.Contain("2 documents");
        });
        _service.Queries.Should().ContainSingle().Which.Should().Be(new DocumentsQuery(null, null, null, 1, 20));
    }

    [Fact]
    public void Query_parameters_flow_into_the_service_call()
    {
        _service.Default = PageOf(2, 10, 23, 3, "a.pdf");
        Guid folder = Guid.NewGuid();

        RenderAt($"documents?folderId={folder}&q=tax&page=2&pageSize=10");

        _service.Queries.Should().ContainSingle()
            .Which.Should().Be(new DocumentsQuery(folder, null, "tax", 2, 10));
    }

    [Fact]
    public void Empty_without_filters_says_no_documents_yet()
    {
        _service.Default = PageOf(1, 20, 0, 0);

        var cut = RenderAt("documents");

        cut.WaitForAssertion(() =>
            cut.Find(".empty-title").TextContent.Should().Be("No documents yet"));
    }

    [Fact]
    public void Empty_with_filters_offers_to_clear_them()
    {
        _service.Default = PageOf(1, 20, 0, 0);

        var cut = RenderAt("documents?q=nothing");

        cut.WaitForAssertion(() =>
        {
            cut.Find(".empty-title").TextContent.Should().Be("No matching documents");
            cut.Find(".empty a").GetAttribute("href").Should().Be("documents");
        });
    }

    [Fact]
    public void A_failed_load_shows_the_problem_and_retry_reloads()
    {
        _service.Results.Enqueue(new DocumentsPageResult(null, new ProblemDetailsView
        {
            Title = "An unexpected error occurred",
            Status = 500,
        }));
        _service.Default = PageOf(1, 20, 1, 1, "invoice.pdf");

        var cut = RenderAt("documents");

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("An unexpected error occurred"));

        cut.Find(".error-retry").Click();

        cut.WaitForAssertion(() =>
            cut.Find("tbody").TextContent.Should().Contain("invoice.pdf"));
    }

    [Fact]
    public void Search_navigates_with_the_text_and_resets_the_page()
    {
        _service.Default = PageOf(3, 20, 60, 3, "a.pdf");

        var cut = RenderAt("documents?page=3");
        cut.WaitForElement("#search").Change("taxes");
        cut.Find("form").Submit();

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.Uri.Should().Contain("q=taxes").And.NotContain("page=");
    }

    [Fact]
    public void Next_navigates_forward_and_previous_is_disabled_on_the_first_page()
    {
        _service.Default = PageOf(1, 20, 60, 3, "a.pdf");

        var cut = RenderAt("documents");

        cut.WaitForAssertion(() => cut.FindAll(".pager button").Count.Should().Be(2));
        var buttons = cut.FindAll(".pager button");
        buttons[0].HasAttribute("disabled").Should().BeTrue("previous is disabled on page 1");
        buttons[1].HasAttribute("disabled").Should().BeFalse();

        buttons[1].Click();

        Services.GetRequiredService<BunitNavigationManager>().Uri.Should().Contain("page=2");
    }

    [Fact]
    public void Next_is_disabled_on_the_last_page()
    {
        _service.Default = PageOf(3, 20, 60, 3, "a.pdf");

        var cut = RenderAt("documents?page=3");

        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll(".pager button");
            buttons[0].HasAttribute("disabled").Should().BeFalse();
            buttons[1].HasAttribute("disabled").Should().BeTrue("next is disabled on the last page");
        });
    }

    [Fact]
    public void Changing_the_page_size_navigates_and_resets_the_page()
    {
        _service.Default = PageOf(2, 20, 60, 3, "a.pdf");

        var cut = RenderAt("documents?page=2");
        cut.WaitForElement(".pager-size select").Change("50");

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.Uri.Should().Contain("pageSize=50").And.NotContain("page=2");
    }
}
