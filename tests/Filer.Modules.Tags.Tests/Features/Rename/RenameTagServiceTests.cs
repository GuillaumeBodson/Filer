using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Features.Rename;
using Filer.Modules.Tags.Persistence;
using Filer.Modules.Tags.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Tags.Tests.Features.Rename;

/// <summary>
/// The rename-tag slice's success paths and every <c>Error</c> path, at its
/// designed seam (12-testing-strategy.md). The owner-scoped EF lookups, the
/// unique-index backstop, and the HTTP status mapping are exercised in
/// Filer.IntegrationTests.
/// </summary>
public sealed class RenameTagServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 16, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITagStore> _tags = new(MockBehavior.Strict);

    private RenameTagService CreateSut(ICurrentUser? caller = null) =>
        new(
            _tags.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<RenameTagService>.Instance);

    /// <summary>A tag owned by the test's caller, as the store would return it.</summary>
    private static Tag OwnedTag(string name) => new()
    {
        OwnerId = OwnerId,
        Name = name,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1),
    };

    private void StoreFinds(Tag tag) =>
        _tags
            .Setup(t => t.FindByIdAsync(OwnerId, tag.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStore()
    {
        var request = new RenameTagRequest("urgent");

        Result<RenameTagResponse> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(Guid.NewGuid(), request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _tags.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenValidationFails_PropagatesTheErrorWithoutTouchingTheStore()
    {
        // One representative failure; every validation rule is pinned in
        // RenameTagValidatorTests.
        var request = new RenameTagRequest("   ");

        Result<RenameTagResponse> result =
            await CreateSut().HandleAsync(Guid.NewGuid(), request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(TagsErrorCodes.NameInvalid);

        _tags.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTheTagIsNotOwned_ReturnsNotFound()
    {
        // Missing and cross-owner are indistinguishable through the owner-scoped
        // lookup (05-security.md).
        var tagId = Guid.NewGuid();
        _tags
            .Setup(t => t.FindByIdAsync(OwnerId, tagId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tag?)null);

        Result<RenameTagResponse> result =
            await CreateSut().HandleAsync(tagId, new RenameTagRequest("urgent"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(TagsErrorCodes.NotFound);

        _tags.Verify(
            t => t.UpdateAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenAnotherTagAlreadyHasTheName_ReturnsConflictWithoutSaving()
    {
        Tag tag = OwnedTag("old");
        StoreFinds(tag);
        _tags
            .Setup(t => t.NameExistsAsync(OwnerId, "urgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Result<RenameTagResponse> result =
            await CreateSut().HandleAsync(tag.Id, new RenameTagRequest("urgent"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(TagsErrorCodes.NameConflict);

        _tags.Verify(
            t => t.UpdateAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithAFreeName_RenamesTrimsAndStampsUpdatedAt()
    {
        Tag tag = OwnedTag("old");
        StoreFinds(tag);
        _tags
            .Setup(t => t.NameExistsAsync(OwnerId, "urgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        Tag? saved = null;
        _tags
            .Setup(t => t.UpdateAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()))
            .Callback<Tag, CancellationToken>((t, _) => saved = t)
            .Returns(Task.CompletedTask);

        Result<RenameTagResponse> result =
            await CreateSut().HandleAsync(tag.Id, new RenameTagRequest("  urgent "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(tag.Id);
        result.Value.Name.Should().Be("urgent");
        result.Value.CreatedAt.Should().Be(Now.AddDays(-1), "creation time is immutable");
        result.Value.UpdatedAt.Should().Be(Now);

        saved.Should().NotBeNull();
        saved!.Name.Should().Be("urgent");
        saved.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task HandleAsync_RenamingToTheCurrentName_SucceedsWithoutCheckingUniqueness()
    {
        // Renaming a tag to its own current name is a no-op that succeeds rather
        // than a self-conflict: the uniqueness pre-check is skipped entirely.
        Tag tag = OwnedTag("urgent");
        StoreFinds(tag);
        _tags
            .Setup(t => t.UpdateAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Result<RenameTagResponse> result =
            await CreateSut().HandleAsync(tag.Id, new RenameTagRequest("urgent"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("urgent");
        result.Value.UpdatedAt.Should().Be(Now);

        _tags.Verify(
            t => t.NameExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_RenamingToTheCurrentNameModuloWhitespace_SucceedsWithoutCheckingUniqueness()
    {
        // The persisted name is the trimmed form, so padding the current name is
        // still the current name — no conflict check, no clash with itself.
        Tag tag = OwnedTag("urgent");
        StoreFinds(tag);
        _tags
            .Setup(t => t.UpdateAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Result<RenameTagResponse> result =
            await CreateSut().HandleAsync(tag.Id, new RenameTagRequest("  urgent "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("urgent");

        _tags.Verify(
            t => t.NameExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
