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
    private readonly Filer.Ui.Tests.Search.FakeSearchService _search = new();

    public DocumentsPageTests()
    {
        Services.AddSingleton<IDocumentsService>(_service);
        Services.AddSingleton<Filer.Ui.Search.ISearchService>(_search);
    }

    private static Filer.Ui.Search.SearchPageResult SearchPageOf(
        int page, int pageSize, long totalCount, int totalPages, params string[] fileNames)
    {
        double score = 0.9;
        return new(new PagedResultOfSearchHitResponse
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = [.. fileNames.Select(name => new SearchHitResponse
            {
                Id = Guid.NewGuid(),
                FileName = name,
                SizeBytes = 128 * 1024,
                Status = "Ready",
                ContentType = "application/pdf",
                CreatedAt = DateTimeOffset.Now,
                Score = score -= 0.1,
            })],
        }, null);
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
        // A folder filter keeps the browse list; a bare ?q= would be search mode.
        _service.Default = PageOf(1, 20, 0, 0);

        var cut = RenderAt($"documents?folderId={Guid.NewGuid()}");

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
        _search.Default = SearchPageOf(1, 20, 1, 1, "taxes.pdf");

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
    public void A_bare_q_enters_ranked_search_mode_and_never_calls_the_list()
    {
        _search.Default = SearchPageOf(1, 20, 2, 1, "facture_2024.pdf", "facture_2023.pdf");

        var cut = RenderAt("documents?q=facture");

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("tbody tr").Count.Should().Be(2);
            cut.Find("tbody").TextContent.Should().Contain("facture_2024.pdf");
            cut.Find(".active-filters").TextContent.Should().Contain("Results for");
            cut.Find(".pager-status").TextContent.Should().Contain("2 results");
        });
        _search.Queries.Should().ContainSingle()
            .Which.Should().Be(new Filer.Ui.Search.SearchQuery("facture", 1, 20));
        _service.Queries.Should().BeEmpty("a bare ?q= is served by /search, not the list");
    }

    [Fact]
    public void A_q_combined_with_a_folder_stays_a_browse_filter()
    {
        _service.Default = PageOf(1, 20, 1, 1, "a.pdf");
        Guid folder = Guid.NewGuid();

        RenderAt($"documents?folderId={folder}&q=tax");

        _service.Queries.Should().ContainSingle()
            .Which.Should().Be(new DocumentsQuery(folder, null, "tax", 1, 20));
        _search.Queries.Should().BeEmpty("scoped ?q= keeps the substring filter semantics");
    }

    [Fact]
    public void An_empty_search_offers_to_clear_it()
    {
        _search.Default = SearchPageOf(1, 20, 0, 0);

        var cut = RenderAt("documents?q=introuvable");

        cut.WaitForAssertion(() =>
        {
            cut.Find(".empty-title").TextContent.Should().Be("No results");
            cut.Find(".empty a").GetAttribute("href").Should().Be("documents");
        });
    }

    [Fact]
    public void A_failed_search_shows_the_problem_and_retry_reloads()
    {
        _search.Results.Enqueue(new Filer.Ui.Search.SearchPageResult(null, new ProblemDetailsView
        {
            Title = "An unexpected error occurred",
            Status = 500,
        }));
        _search.Default = SearchPageOf(1, 20, 1, 1, "facture.pdf");

        var cut = RenderAt("documents?q=facture");

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("An unexpected error occurred"));

        cut.Find(".error-retry").Click();

        cut.WaitForAssertion(() =>
            cut.Find("tbody").TextContent.Should().Contain("facture.pdf"));
    }

    [Fact]
    public void Search_mode_pagination_navigates_with_the_query()
    {
        _search.Default = SearchPageOf(1, 20, 60, 3, "a.pdf");

        var cut = RenderAt("documents?q=facture");

        cut.WaitForAssertion(() => cut.FindAll(".pager button").Count.Should().Be(2));
        cut.FindAll(".pager button")[1].Click();

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.Uri.Should().Contain("q=facture").And.Contain("page=2");
    }

    [Fact]
    public void Submitting_a_search_leaves_the_folder_context()
    {
        _service.Default = PageOf(1, 20, 1, 1, "a.pdf");
        _search.Default = SearchPageOf(1, 20, 1, 1, "facture.pdf");
        Guid folder = Guid.NewGuid();

        var cut = RenderAt($"documents?folderId={folder}");
        cut.WaitForElement("#search").Change("facture");
        cut.Find("form[role=search]").Submit();

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.Uri.Should().Contain("q=facture").And.NotContain("folderId=");
    }

    [Fact]
    public void Clearing_the_search_box_returns_to_the_browse_list()
    {
        _search.Default = SearchPageOf(1, 20, 1, 1, "facture.pdf");
        _service.Default = PageOf(1, 20, 3, 1, "a.pdf", "b.pdf", "c.pdf");

        var cut = RenderAt("documents?q=facture");
        cut.WaitForElement("#search").Change("");
        cut.Find("form[role=search]").Submit();

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.Uri.Should().NotContain("q=");
        cut.WaitForAssertion(() =>
            cut.Find(".pager-status").TextContent.Should().Contain("3 documents"));
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
