using System.Net;
using Filer.ApiClient.Generated;
using Filer.Ui.Search;
using Filer.Ui.Tests.Auth;
using FluentAssertions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Filer.Ui.Tests.Search;

/// <summary>
/// Exercises <see cref="SearchService"/> through the real Kiota client against a
/// stubbed transport: query serialization, the ranked envelope mapping, and the
/// problem-details contract (#169) on declared error statuses.
/// </summary>
public sealed class SearchServiceTests
{
    private static SearchService CreateService(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.test/"),
        };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient)
        {
            BaseUrl = "https://api.test/",
        };
        return new SearchService(new FilerApiClient(adapter));
    }

    [Fact]
    public async Task Search_serializes_the_query_and_maps_the_ranked_envelope()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        {
          "items": [
            { "id": "11111111-1111-1111-1111-111111111111", "fileName": "facture_2024.pdf",
              "contentType": "application/pdf", "sizeBytes": 131072, "status": "Ready",
              "createdAt": "2026-07-01T10:00:00+00:00", "score": 0.62 },
            { "id": "22222222-2222-2222-2222-222222222222", "fileName": "notes.pdf",
              "contentType": "application/pdf", "sizeBytes": 2048, "status": "Ready",
              "createdAt": "2026-07-02T10:00:00+00:00", "score": 0.31 }
          ],
          "page": 2, "pageSize": 10, "totalCount": 12, "totalPages": 2
        }
        """);
        SearchService service = CreateService(inner);

        SearchPageResult result = await service.SearchAsync(
            new SearchQuery("facture", Page: 2, PageSize: 10),
            TestContext.Current.CancellationToken);

        result.Problem.Should().BeNull();
        result.Page!.TotalCount.Should().Be(12);
        result.Page.TotalPages.Should().Be(2);
        result.Page.Items.Should().HaveCount(2);
        result.Page.Items![0].FileName.Should().Be("facture_2024.pdf");
        result.Page.Items[0].Score.Should().Be(0.62);

        var request = inner.Requests.Should().ContainSingle().Which;
        request.RequestUri!.AbsolutePath.Should().Be("/api/v1/search");
        request.RequestUri.Query.Should()
            .Contain("q=facture").And.Contain("page=2").And.Contain("pageSize=10");
    }

    [Fact]
    public async Task A_declared_400_surfaces_the_problem_with_its_code()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.BadRequest, """
        {
          "type": "https://docs/errors/search_term_invalid",
          "title": "Validation failed",
          "status": 400,
          "detail": "The q search term is required.",
          "code": "search_term_invalid"
        }
        """);
        SearchService service = CreateService(inner);

        SearchPageResult result = await service.SearchAsync(
            new SearchQuery("   "), TestContext.Current.CancellationToken);

        result.Page.Should().BeNull();
        result.Problem!.Code.Should().Be("search_term_invalid");
        result.Problem.Title.Should().Be("Validation failed");
    }

    [Fact]
    public async Task A_declared_401_surfaces_as_a_problem()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Unauthorized, """
        { "type": "https://docs/errors/unauthorized", "title": "Authentication failed",
          "status": 401, "detail": "Authentication required.", "code": "unauthorized" }
        """);
        SearchService service = CreateService(inner);

        SearchPageResult result = await service.SearchAsync(
            new SearchQuery("facture"), TestContext.Current.CancellationToken);

        result.Page.Should().BeNull();
        result.Problem!.Status.Should().Be(401);
    }
}
