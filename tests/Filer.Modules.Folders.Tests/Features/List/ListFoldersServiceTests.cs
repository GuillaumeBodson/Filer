using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Features.List;
using Filer.Modules.Folders.Persistence;
using Filer.Modules.Folders.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Folders.Tests.Features.List;

/// <summary>
/// The list-folders slice's success paths and every <c>Error</c> path, at its
/// designed seam (12-testing-strategy.md) — including the tree assembly the
/// issue pins a unit test on (#41). The owner-scoped EF query, soft-delete
/// exclusion in SQL, and the HTTP status mapping are exercised in
/// Filer.IntegrationTests.
/// </summary>
public sealed class ListFoldersServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly Mock<IFolderStore> _folders = new(MockBehavior.Strict);

    private ListFoldersService CreateSut(ICurrentUser? caller = null) =>
        new(
            _folders.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<ListFoldersService>.Instance);

    private void StoreReturns(params Folder[] folders) =>
        _folders
            .Setup(f => f.ListActiveAsync(OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

    /// <summary>A folder owned by the test's caller, as the store would return it.</summary>
    private static Folder OwnedFolder(string name, Guid? parentId = null) => new()
    {
        OwnerId = OwnerId,
        ParentId = parentId,
        Name = name,
    };

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStore()
    {
        Result<IReadOnlyList<FolderListItemResponse>> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(new ListFoldersQuery(null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _folders.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTheViewIsUnknown_ReturnsValidationErrorWithoutTouchingTheStore()
    {
        // One representative failure; every parsing rule is pinned in
        // ListFoldersValidatorTests.
        Result<IReadOnlyList<FolderListItemResponse>> result = await CreateSut()
            .HandleAsync(new ListFoldersQuery("nested"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(FoldersErrorCodes.ViewInvalid);

        _folders.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WithoutAView_ReturnsTheFlatShape()
    {
        // The default is flat (03-api-specification.md): every folder at the top
        // level of the payload, no Children property, store order preserved.
        Folder parent = OwnedFolder("Archive");
        Folder child = OwnedFolder("Invoices", parent.Id);
        StoreReturns(parent, child);

        Result<IReadOnlyList<FolderListItemResponse>> result = await CreateSut()
            .HandleAsync(new ListFoldersQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(i => i.Name).Should().ContainInOrder("Archive", "Invoices");
        result.Value.Should().OnlyContain(
            i => i.Children == null, "the flat view omits the children property");

        FolderListItemResponse nested = result.Value.Single(i => i.Name == "Invoices");
        nested.ParentId.Should().Be(parent.Id);
        nested.Id.Should().Be(child.Id);
        nested.CreatedAt.Should().Be(child.CreatedAt);
        nested.UpdatedAt.Should().Be(child.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_WithTheTreeView_NestsChildrenUnderTheirParents()
    {
        // Archive ─ 2026 ─ Q1, plus a second root; the store returns name order
        // (2026, Archive, Inbox, Q1) and assembly must nest without losing it.
        Folder archive = OwnedFolder("Archive");
        Folder year = OwnedFolder("2026", archive.Id);
        Folder quarter = OwnedFolder("Q1", year.Id);
        Folder inbox = OwnedFolder("Inbox");
        StoreReturns(year, archive, inbox, quarter);

        Result<IReadOnlyList<FolderListItemResponse>> result = await CreateSut()
            .HandleAsync(new ListFoldersQuery("tree"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(r => r.Name).Should().ContainInOrder("Archive", "Inbox");

        FolderListItemResponse root = result.Value.Single(r => r.Name == "Archive");
        FolderListItemResponse nestedYear = root.Children.Should().ContainSingle().Subject;
        nestedYear.Name.Should().Be("2026");
        nestedYear.ParentId.Should().Be(archive.Id);

        FolderListItemResponse nestedQuarter = nestedYear.Children.Should().ContainSingle().Subject;
        nestedQuarter.Name.Should().Be("Q1");
        nestedQuarter.Children.Should().BeEmpty("tree leaves carry an empty children list, not null");

        result.Value.Single(r => r.Name == "Inbox").Children.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithTheTreeView_OrdersSiblingsByTheStoreOrder()
    {
        Folder root = OwnedFolder("Root");
        Folder alpha = OwnedFolder("Alpha", root.Id);
        Folder beta = OwnedFolder("Beta", root.Id);
        StoreReturns(alpha, beta, root);

        Result<IReadOnlyList<FolderListItemResponse>> result = await CreateSut()
            .HandleAsync(new ListFoldersQuery("tree"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Single().Children!.Select(c => c.Name)
            .Should().ContainInOrder("Alpha", "Beta");
    }

    [Fact]
    public async Task HandleAsync_WithTheTreeView_SurfacesAFolderWithAMissingParentAsARoot()
    {
        // ADR-007 forbids an active child under a deleted parent, but if the state
        // ever occurs the folder must surface at the root rather than silently
        // vanish from the tree.
        Folder orphan = OwnedFolder("Orphan", parentId: Guid.NewGuid());
        StoreReturns(orphan);

        Result<IReadOnlyList<FolderListItemResponse>> result = await CreateSut()
            .HandleAsync(new ListFoldersQuery("tree"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        FolderListItemResponse root = result.Value.Should().ContainSingle().Subject;
        root.Name.Should().Be("Orphan");
        root.Children.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tree")]
    public async Task HandleAsync_WithoutAnyFolders_ReturnsAnEmptyList(string? view)
    {
        StoreReturns();

        Result<IReadOnlyList<FolderListItemResponse>> result = await CreateSut()
            .HandleAsync(new ListFoldersQuery(view), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
