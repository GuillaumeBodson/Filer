using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Features.Get;
using Filer.Modules.Folders.Persistence;
using Filer.Modules.Folders.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Folders.Tests.Features.Get;

/// <summary>
/// The get-folder slice's success path and every <c>Error</c> path, at its
/// designed seam (12-testing-strategy.md). The owner-scoped EF lookup and the
/// HTTP status mapping are exercised in Filer.IntegrationTests.
/// </summary>
public sealed class GetFolderServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid FolderId = Guid.NewGuid();

    private readonly Mock<IFolderStore> _folders = new(MockBehavior.Strict);

    private GetFolderService CreateSut(ICurrentUser? caller = null) =>
        new(
            _folders.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            NullLogger<GetFolderService>.Instance);

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStore()
    {
        Result<GetFolderResponse> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(FolderId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _folders.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTheFolderIsNotOwned_ReturnsNotFound()
    {
        // One arrangement, three real-world causes — missing id, another owner's
        // folder, soft-deleted folder — because the owner-scoped store lookup is
        // the single chokepoint that makes them indistinguishable (05-security.md).
        _folders
            .Setup(f => f.FindActiveByIdAsync(OwnerId, FolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        Result<GetFolderResponse> result =
            await CreateSut().HandleAsync(FolderId, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(FoldersErrorCodes.FolderNotFound);
    }

    [Fact]
    public async Task HandleAsync_WhenTheCallerOwnsTheFolder_ReturnsTheDto()
    {
        var parentId = Guid.NewGuid();
        var folder = new Folder
        {
            Id = FolderId,
            OwnerId = OwnerId,
            ParentId = parentId,
            Name = "Invoices",
            CreatedAt = new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 6, 7, 13, 0, 0, TimeSpan.Zero),
        };
        _folders
            .Setup(f => f.FindActiveByIdAsync(OwnerId, FolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);

        Result<GetFolderResponse> result =
            await CreateSut().HandleAsync(FolderId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(FolderId);
        result.Value.ParentId.Should().Be(parentId);
        result.Value.Name.Should().Be("Invoices");
        result.Value.CreatedAt.Should().Be(folder.CreatedAt);
        result.Value.UpdatedAt.Should().Be(folder.UpdatedAt);
    }
}
