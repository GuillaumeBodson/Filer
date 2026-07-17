using System.Net;
using Filer.ApiClient.Generated;
using Filer.Ui.Folders;
using Filer.Ui.Models;
using Filer.Ui.Tests.Auth;
using FluentAssertions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Xunit;

namespace Filer.Ui.Tests.Folders;

/// <summary>
/// Exercises <see cref="FoldersService"/> through the real Kiota client against a
/// stubbed transport: bodies on the wire, the recursive opt-in, and the cycle 409.
/// </summary>
public sealed class FoldersServiceTests
{
    private static readonly Guid FolderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static FoldersService CreateService(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.test/"),
        };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient)
        {
            BaseUrl = "https://api.test/",
        };
        return new FoldersService(new FilerApiClient(adapter));
    }

    [Fact]
    public async Task Create_posts_name_and_parent()
    {
        var parent = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Created, """
        { "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "name": "2026",
          "parentId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" }
        """);
        FoldersService service = CreateService(inner);

        FolderCreateResult result = await service.CreateAsync("2026", parent, TestContext.Current.CancellationToken);

        result.Folder!.Name.Should().Be("2026");
        string body = System.Text.Encoding.UTF8.GetString(inner.Requests.Single().Body!);
        body.Should().Contain("\"name\":\"2026\"").And.Contain(parent.ToString());
    }

    [Fact]
    public async Task Moving_to_the_root_sends_an_explicit_null_parentId()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, """
        { "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "name": "Taxes" }
        """);
        FoldersService service = CreateService(inner);

        await service.UpdateAsync(
            FolderId, new FolderUpdate { MoveParent = true, TargetParentId = null },
            TestContext.Current.CancellationToken);

        string body = System.Text.Encoding.UTF8.GetString(inner.Requests.Single().Body!);
        body.Replace(" ", "", StringComparison.Ordinal).Should().Contain("\"parentId\":null");
        body.Should().NotContain("\"name\"");
    }

    [Fact]
    public async Task The_cycle_409_surfaces_with_its_code()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Conflict, """
        { "type": "https://docs/errors/folder_move_cycle", "title": "Conflict",
          "status": 409, "detail": "Moving the folder there would create a cycle.",
          "code": "folder_move_cycle" }
        """);
        FoldersService service = CreateService(inner);

        FolderUpdateResult result = await service.UpdateAsync(
            FolderId, new FolderUpdate { MoveParent = true, TargetParentId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        result.Problem!.Code.Should().Be("folder_move_cycle");
    }

    [Fact]
    public async Task Delete_sends_recursive_only_when_opted_in()
    {
        var inner = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.NoContent)
            .Enqueue(HttpStatusCode.NoContent);
        FoldersService service = CreateService(inner);

        (await service.DeleteAsync(FolderId, recursive: false, TestContext.Current.CancellationToken)).Should().BeNull();
        (await service.DeleteAsync(FolderId, recursive: true, TestContext.Current.CancellationToken)).Should().BeNull();

        inner.Requests[0].RequestUri!.Query.Should().NotContain("recursive");
        inner.Requests[1].RequestUri!.Query.Should().Contain("recursive=true");
    }

    [Fact]
    public async Task The_non_empty_409_surfaces_with_its_code()
    {
        var inner = new StubHttpMessageHandler().Enqueue(HttpStatusCode.Conflict, """
        { "type": "https://docs/errors/folder_not_empty", "title": "Conflict",
          "status": 409, "detail": "The folder is not empty.", "code": "folder_not_empty" }
        """);
        FoldersService service = CreateService(inner);

        ProblemDetailsView? problem = await service.DeleteAsync(
            FolderId, recursive: false, TestContext.Current.CancellationToken);

        problem!.Code.Should().Be("folder_not_empty");
    }
}
