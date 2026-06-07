using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Features.Create;
using Filer.Modules.Folders.Persistence;
using Filer.Modules.Folders.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Folders.Tests.Features.Create;

/// <summary>
/// The create-folder slice's success paths and every <c>Error</c> path, at its
/// designed seam (12-testing-strategy.md). The owner-scoped EF lookups, the
/// unique-index backstop, and the HTTP status mapping are exercised in
/// Filer.IntegrationTests.
/// </summary>
public sealed class CreateFolderServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IFolderStore> _folders = new(MockBehavior.Strict);

    private CreateFolderService CreateSut(ICurrentUser? caller = null) =>
        new(
            _folders.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<CreateFolderService>.Instance);

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStore()
    {
        var request = new CreateFolderRequest("Invoices", null);

        Result<CreateFolderResponse> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _folders.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenValidationFails_PropagatesTheErrorWithoutTouchingTheStore()
    {
        // One representative failure; every validation rule is pinned in
        // CreateFolderValidatorTests.
        var request = new CreateFolderRequest("   ", null);

        Result<CreateFolderResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(FoldersErrorCodes.NameInvalid);

        _folders.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenParentIsNotOwned_ReturnsNotFoundWithoutSaving()
    {
        // One arrangement, three real-world causes — missing id, another owner's
        // folder, soft-deleted folder — because the owner-scoped store lookup is
        // the single chokepoint that makes them indistinguishable (05-security.md).
        _folders
            .Setup(f => f.ActiveExistsAsync(OwnerId, ParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new CreateFolderRequest("Invoices", ParentId);

        Result<CreateFolderResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(FoldersErrorCodes.ParentNotFound);

        _folders.Verify(
            f => f.AddAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenASiblingHasTheSameName_ReturnsConflictWithoutSaving()
    {
        _folders
            .Setup(f => f.ActiveSiblingNameExistsAsync(OwnerId, null, "Invoices", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new CreateFolderRequest("Invoices", null);

        Result<CreateFolderResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(FoldersErrorCodes.NameConflict);

        _folders.Verify(
            f => f.AddAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AtTopLevel_PersistsAndReturnsTheCreatedFolder()
    {
        Folder? saved = null;
        _folders
            .Setup(f => f.ActiveSiblingNameExistsAsync(OwnerId, null, "Invoices", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders
            .Setup(f => f.AddAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()))
            .Callback<Folder, CancellationToken>((folder, _) => saved = folder)
            .Returns(Task.CompletedTask);

        var request = new CreateFolderRequest("Invoices", null);

        Result<CreateFolderResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Invoices");
        result.Value.ParentId.Should().BeNull();
        result.Value.CreatedAt.Should().Be(Now);
        result.Value.UpdatedAt.Should().Be(Now);

        saved.Should().NotBeNull();
        saved!.OwnerId.Should().Be(OwnerId);
        saved.TenantId.Should().BeNull("TenantId stays null in V1 (02-data-model.md)");
        saved.DeletedAt.Should().BeNull();
        result.Value.Id.Should().Be(saved.Id);

        // No parent to verify: the top level needs no ownership check.
        _folders.Verify(
            f => f.ActiveExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UnderAnOwnedParent_PersistsTheParentReference()
    {
        _folders
            .Setup(f => f.ActiveExistsAsync(OwnerId, ParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _folders
            .Setup(f => f.ActiveSiblingNameExistsAsync(OwnerId, ParentId, "2026", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders
            .Setup(f => f.AddAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CreateFolderRequest("2026", ParentId);

        Result<CreateFolderResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParentId.Should().Be(ParentId);
    }

    [Fact]
    public async Task HandleAsync_TrimsTheNameBeforeCheckingAndPersisting()
    {
        // "  Inbox " and "Inbox" are the same folder: the sibling check and the
        // persisted row both use the trimmed form.
        Folder? saved = null;
        _folders
            .Setup(f => f.ActiveSiblingNameExistsAsync(OwnerId, null, "Inbox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _folders
            .Setup(f => f.AddAsync(It.IsAny<Folder>(), It.IsAny<CancellationToken>()))
            .Callback<Folder, CancellationToken>((folder, _) => saved = folder)
            .Returns(Task.CompletedTask);

        var request = new CreateFolderRequest("  Inbox ", null);

        Result<CreateFolderResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Inbox");
        saved!.Name.Should().Be("Inbox");
    }
}
