using Filer.Modules.Documents.Contracts;
using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Features.Delete;
using Filer.Modules.Folders.Persistence;
using Filer.Modules.Folders.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Folders.Tests.Features.Delete;

/// <summary>
/// The delete slice's success paths and every <c>Error</c> path at its designed
/// seam (12-testing-strategy.md): ADR-007's reject-by-default 409, the recursive
/// cascade over the subtree with one shared timestamp, and the documents-first
/// ordering. The owner-scoped EF writes and the HTTP status mapping are
/// exercised in Filer.IntegrationTests.
/// </summary>
public sealed class DeleteFolderServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 17, 0, 0, TimeSpan.Zero);

    private readonly Mock<IFolderStore> _folders = new(MockBehavior.Strict);
    private readonly Mock<IFolderDocumentRemover> _documents = new(MockBehavior.Strict);

    private DeleteFolderService CreateSut(ICurrentUser? caller = null) =>
        new(
            _folders.Object,
            _documents.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<DeleteFolderService>.Instance);

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

    private void StoreLists(params Folder[] owned) =>
        _folders
            .Setup(f => f.ListActiveAsync(OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(owned);

    private List<Guid>? CaptureFolderDelete()
    {
        var captured = new List<Guid>();
        _folders
            .Setup(f => f.SoftDeleteAsync(
                OwnerId, It.IsAny<IReadOnlyCollection<Guid>>(), Now, It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<Guid>, DateTimeOffset, CancellationToken>(
                (_, ids, _, _) => captured.AddRange(ids))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, DateTimeOffset _, CancellationToken _) => ids.Count);
        return captured;
    }

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStores()
    {
        Result result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(Guid.NewGuid(), recursive: false, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _folders.VerifyNoOtherCalls();
        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTheFolderIsNotOwned_ReturnsNotFound()
    {
        // Missing, cross-owner, and soft-deleted are indistinguishable through the
        // owner-scoped lookup (05-security.md) — a repeat delete lands here too.
        var folderId = Guid.NewGuid();
        _folders
            .Setup(f => f.FindActiveByIdAsync(OwnerId, folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        Result result = await CreateSut()
            .HandleAsync(folderId, recursive: false, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(FoldersErrorCodes.FolderNotFound);

        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_NonRecursiveWithAChildFolder_ReturnsNotEmptyWithoutDeleting()
    {
        Folder folder = OwnedFolder("Parent");
        Folder child = OwnedFolder("Child", folder.Id);
        StoreFinds(folder);
        StoreLists(folder, child);

        Result result = await CreateSut()
            .HandleAsync(folder.Id, recursive: false, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(FoldersErrorCodes.NotEmpty);

        // The child folder already decides the 409: the document check is not
        // needed, and nothing is deleted anywhere.
        _documents.VerifyNoOtherCalls();
        _folders.Verify(
            f => f.SoftDeleteAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NonRecursiveWithADocumentInside_ReturnsNotEmptyWithoutDeleting()
    {
        Folder folder = OwnedFolder("WithDocument");
        StoreFinds(folder);
        StoreLists(folder);
        _documents
            .Setup(d => d.AnyActiveInFolderAsync(OwnerId, folder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Result result = await CreateSut()
            .HandleAsync(folder.Id, recursive: false, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(FoldersErrorCodes.NotEmpty);

        _folders.Verify(
            f => f.SoftDeleteAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NonRecursiveEmptyFolder_SoftDeletesJustThatFolder()
    {
        Folder folder = OwnedFolder("Empty");
        Folder unrelated = OwnedFolder("Unrelated");
        StoreFinds(folder);
        StoreLists(folder, unrelated);
        _documents
            .Setup(d => d.AnyActiveInFolderAsync(OwnerId, folder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        List<Guid>? deleted = CaptureFolderDelete();

        Result result = await CreateSut()
            .HandleAsync(folder.Id, recursive: false, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        deleted.Should().Equal(folder.Id);

        // An empty folder has no documents to cascade — proven by the emptiness
        // check, so the remover is never asked to delete anything.
        _documents.Verify(
            d => d.SoftDeleteInFoldersAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Recursive_CascadesTheExactSubtreeWithOneTimestamp()
    {
        // Root ─ Child ─ Grandchild, plus an unrelated sibling tree that must
        // survive: the cascade is the subtree, nothing else (ADR-007).
        Folder root = OwnedFolder("Root");
        Folder child = OwnedFolder("Child", root.Id);
        Folder grandchild = OwnedFolder("Grandchild", child.Id);
        Folder unrelated = OwnedFolder("Unrelated");
        Folder unrelatedChild = OwnedFolder("UnrelatedChild", unrelated.Id);
        StoreFinds(root);
        StoreLists(root, child, grandchild, unrelated, unrelatedChild);

        IReadOnlyCollection<Guid>? documentScope = null;
        _documents
            .Setup(d => d.SoftDeleteInFoldersAsync(
                OwnerId, It.IsAny<IReadOnlyCollection<Guid>>(), Now, It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<Guid>, DateTimeOffset, CancellationToken>(
                (_, ids, _, _) => documentScope = ids)
            .ReturnsAsync(Result.Success(2));
        List<Guid>? foldersDeleted = CaptureFolderDelete();

        Result result = await CreateSut()
            .HandleAsync(root.Id, recursive: true, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        Guid[] subtree = [root.Id, child.Id, grandchild.Id];
        foldersDeleted.Should().BeEquivalentTo(subtree);
        documentScope.Should().BeEquivalentTo(
            subtree, "documents are removed from exactly the deleted folders, with the same timestamp");

        // Recursive opts out of the emptiness check by definition.
        _documents.Verify(
            d => d.AnyActiveInFolderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_RecursiveOnALeafWithDocuments_DeletesWithoutAnEmptinessCheck()
    {
        Folder folder = OwnedFolder("Leaf");
        StoreFinds(folder);
        StoreLists(folder);
        _documents
            .Setup(d => d.SoftDeleteInFoldersAsync(
                OwnerId, It.IsAny<IReadOnlyCollection<Guid>>(), Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(3));
        List<Guid>? foldersDeleted = CaptureFolderDelete();

        Result result = await CreateSut()
            .HandleAsync(folder.Id, recursive: true, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        foldersDeleted.Should().Equal(folder.Id);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDocumentCascadeFails_PropagatesAndDeletesNoFolder()
    {
        // Documents-first ordering: a failed document cascade must leave every
        // folder standing, so a retry can complete the delete.
        Folder folder = OwnedFolder("Doomed");
        StoreFinds(folder);
        StoreLists(folder);
        var error = Error.Validation("The document id is required.", "job_document_id_required");
        _documents
            .Setup(d => d.SoftDeleteInFoldersAsync(
                OwnerId, It.IsAny<IReadOnlyCollection<Guid>>(), Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<int>(error));

        Result result = await CreateSut()
            .HandleAsync(folder.Id, recursive: true, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeSameAs(error);

        _folders.Verify(
            f => f.SoftDeleteAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
