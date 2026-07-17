using System.Net;
using Filer.ApiClient.Generated;
using Filer.Ui.Documents;
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
}
