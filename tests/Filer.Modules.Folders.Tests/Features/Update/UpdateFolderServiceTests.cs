using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Features.Update;
using Filer.Modules.Folders.Persistence;
using Filer.Modules.Folders.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Folders.Tests.Features.Update;

/// <summary>
/// The rename/move slice's success paths and every <c>Error</c> path — including
/// the cycle prevention the issue pins unit tests on (#43, 02-data-model.md) —
/// at its designed seam (12-testing-strategy.md). The owner-scoped EF lookups,
/// the unique-index backstop, and the HTTP status mapping are exercised in
/// Filer.IntegrationTests.
/// </summary>
public sealed class UpdateFolderServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 16, 0, 0, TimeSpan.Zero);

    private readonly Mock<IFolderStore> _folders = new(MockBehavior.Strict);

    private UpdateFolderService CreateSut(ICurrentUser? caller = null) =>
        new(
            _folders.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<UpdateFolderService>.Instance);

    /// <summary>A folder owned by the test's caller, as the store would return it.</summary>
    private static Folder OwnedFolder(string name, Guid? parentId = null) => new()
    {
        OwnerId = OwnerId,
        ParentId = parentId,
        Name = name,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1),
    };

    private void StoreFinds(Folder folder) =>
        _folders
            .Setup(f => f.FindActiveByIdAsync(OwnerId, folder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);

    private void StoreHasActive(params Folder[] owned)
    {
        foreach (Folder folder in owned)
        {
            _folders
                .Setup(f => f.ActiveExistsAsync(OwnerId, folder.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        _folders
            .Setup(f => f.ListActiveAsync(OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(owned);
    }

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStore()
    {
        var request = new UpdateFolderRequest { Name = "Renamed" };

        Result<UpdateFolderResponse> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(Guid.NewGuid(), request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _folders.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenValidationFails_PropagatesTheErrorWithoutTouchingTheStore()
    {
        // One representative failure; every validation rule is pinned in
        // UpdateFolderValidatorTests.
        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(Guid.NewGuid(), new UpdateFolderRequest(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(FoldersErrorCodes.UpdateEmpty);

        _folders.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTheFolderIsNotOwned_ReturnsNotFound()
    {
        // Missing, cross-owner, and soft-deleted are indistinguishable through the
        // owner-scoped lookup (05-security.md).
        var folderId = Guid.NewGuid();
        _folders
            .Setup(f => f.FindActiveByIdAsync(OwnerId, folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(folderId, new UpdateFolderRequest { Name = "Renamed" }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(FoldersErrorCodes.FolderNotFound);
    }

    [Fact]
    public async Task HandleAsync_RenameOnly_TrimsPersistsAndStampsUpdatedAt()
    {
        Folder folder = OwnedFolder("Old", parentId: Guid.NewGuid());
        StoreFinds(folder);
        _folders
            .Setup(f => f.ActiveSiblingNameExistsAsync(
                OwnerId, folder.ParentId, "Renamed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders
            .Setup(f => f.UpdateAsync(folder, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateFolderRequest { Name = "  Renamed " };

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(folder.Id, request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Renamed");
        result.Value.ParentId.Should().Be(folder.ParentId, "an absent parentId leaves the parent untouched");
        result.Value.UpdatedAt.Should().Be(Now);
        folder.Name.Should().Be("Renamed");

        // A rename never needs the target-parent checks.
        _folders.Verify(
            f => f.ActiveExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _folders.Verify(
            f => f.ListActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MoveUnderAnOwnedNonDescendant_Persists()
    {
        Folder folder = OwnedFolder("Movable");
        Folder target = OwnedFolder("Target");
        StoreFinds(folder);
        StoreHasActive(folder, target);
        _folders
            .Setup(f => f.ActiveSiblingNameExistsAsync(
                OwnerId, target.Id, "Movable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders
            .Setup(f => f.UpdateAsync(folder, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateFolderRequest { ParentId = target.Id };

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(folder.Id, request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParentId.Should().Be(target.Id);
        result.Value.Name.Should().Be("Movable", "an absent name leaves the name untouched");
        folder.ParentId.Should().Be(target.Id);
    }

    [Fact]
    public async Task HandleAsync_MoveToTheTopLevel_NeedsNoTargetChecks()
    {
        Folder parent = OwnedFolder("Parent");
        Folder folder = OwnedFolder("Nested", parent.Id);
        StoreFinds(folder);
        _folders
            .Setup(f => f.ActiveSiblingNameExistsAsync(
                OwnerId, null, "Nested", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders
            .Setup(f => f.UpdateAsync(folder, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateFolderRequest { ParentId = null };

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(folder.Id, request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParentId.Should().BeNull();

        // Explicit null is the top level: no existence check, no cycle walk.
        _folders.Verify(
            f => f.ActiveExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _folders.Verify(
            f => f.ListActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenTheTargetParentIsNotOwned_ReturnsNotFoundWithoutSaving()
    {
        Folder folder = OwnedFolder("Movable");
        var foreignParentId = Guid.NewGuid();
        StoreFinds(folder);
        _folders
            .Setup(f => f.ActiveExistsAsync(OwnerId, foreignParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new UpdateFolderRequest { ParentId = foreignParentId };

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(folder.Id, request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(FoldersErrorCodes.ParentNotFound);

        _folders.Verify(
            f => f.UpdateAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MoveUnderItself_ReturnsMoveCycleWithoutSaving()
    {
        Folder folder = OwnedFolder("Loop");
        StoreFinds(folder);
        StoreHasActive(folder);

        var request = new UpdateFolderRequest { ParentId = folder.Id };

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(folder.Id, request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(FoldersErrorCodes.MoveCycle);

        _folders.Verify(
            f => f.UpdateAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MoveUnderADescendant_ReturnsMoveCycleWithoutSaving()
    {
        // Root ─ Child ─ Grandchild; moving Root under Grandchild would make Root
        // its own ancestor — the cycle the walk must catch (02-data-model.md).
        Folder root = OwnedFolder("Root");
        Folder child = OwnedFolder("Child", root.Id);
        Folder grandchild = OwnedFolder("Grandchild", child.Id);
        StoreFinds(root);
        StoreHasActive(root, child, grandchild);

        var request = new UpdateFolderRequest { ParentId = grandchild.Id };

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(root.Id, request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(FoldersErrorCodes.MoveCycle);

        _folders.Verify(
            f => f.UpdateAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MoveUnderASibling_IsNotACycle()
    {
        // The walk goes upward from the target: a sibling's chain never visits the
        // folder, so the move is legal.
        Folder parent = OwnedFolder("Parent");
        Folder folder = OwnedFolder("A", parent.Id);
        Folder sibling = OwnedFolder("B", parent.Id);
        StoreFinds(folder);
        StoreHasActive(parent, folder, sibling);
        _folders
            .Setup(f => f.ActiveSiblingNameExistsAsync(
                OwnerId, sibling.Id, "A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders
            .Setup(f => f.UpdateAsync(folder, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateFolderRequest { ParentId = sibling.Id };

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(folder.Id, request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParentId.Should().Be(sibling.Id);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNewPairCollidesWithASibling_ReturnsConflictWithoutSaving()
    {
        Folder folder = OwnedFolder("Old");
        StoreFinds(folder);
        _folders
            .Setup(f => f.ActiveSiblingNameExistsAsync(
                OwnerId, null, "Taken", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new UpdateFolderRequest { Name = "Taken" };

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(folder.Id, request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(FoldersErrorCodes.NameConflict);

        _folders.Verify(
            f => f.UpdateAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenThePatchRestatesTheCurrentValues_SkipsTheSiblingCheckAndSaves()
    {
        // The unchanged (parent, name) pair could only ever match the folder's own
        // row — running the check would misreport it as a conflict.
        Folder folder = OwnedFolder("Same");
        StoreFinds(folder);
        _folders
            .Setup(f => f.UpdateAsync(folder, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateFolderRequest { Name = "Same" };

        Result<UpdateFolderResponse> result = await CreateSut()
            .HandleAsync(folder.Id, request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UpdatedAt.Should().Be(Now);

        _folders.Verify(
            f => f.ActiveSiblingNameExistsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
