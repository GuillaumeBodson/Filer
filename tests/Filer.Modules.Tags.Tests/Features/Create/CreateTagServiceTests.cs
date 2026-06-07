using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Features.Create;
using Filer.Modules.Tags.Persistence;
using Filer.Modules.Tags.Tests.TestSupport;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Tags.Tests.Features.Create;

/// <summary>
/// The create-tag slice's success paths and every <c>Error</c> path, at its
/// designed seam (12-testing-strategy.md). The owner-scoped EF lookups, the
/// unique-index backstop, and the HTTP status mapping are exercised in
/// Filer.IntegrationTests.
/// </summary>
public sealed class CreateTagServiceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ITagStore> _tags = new(MockBehavior.Strict);

    private CreateTagService CreateSut(ICurrentUser? caller = null) =>
        new(
            _tags.Object,
            caller ?? new StubCurrentUser(true, OwnerId),
            new FixedClock(Now),
            NullLogger<CreateTagService>.Instance);

    [Fact]
    public async Task HandleAsync_WhenCallerUnauthenticated_ReturnsUnauthorizedWithoutTouchingTheStore()
    {
        var request = new CreateTagRequest("urgent");

        Result<CreateTagResponse> result = await CreateSut(StubCurrentUser.Anonymous)
            .HandleAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);

        _tags.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenValidationFails_PropagatesTheErrorWithoutTouchingTheStore()
    {
        // One representative failure; every validation rule is pinned in
        // CreateTagValidatorTests.
        var request = new CreateTagRequest("   ");

        Result<CreateTagResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(TagsErrorCodes.NameInvalid);

        _tags.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenTheOwnerAlreadyHasTheName_ReturnsConflictWithoutSaving()
    {
        _tags
            .Setup(t => t.NameExistsAsync(OwnerId, "urgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new CreateTagRequest("urgent");

        Result<CreateTagResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(TagsErrorCodes.NameConflict);

        _tags.Verify(
            t => t.AddAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithAFreeName_PersistsAndReturnsTheCreatedTag()
    {
        Tag? saved = null;
        _tags
            .Setup(t => t.NameExistsAsync(OwnerId, "urgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tags
            .Setup(t => t.AddAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()))
            .Callback<Tag, CancellationToken>((tag, _) => saved = tag)
            .Returns(Task.CompletedTask);

        var request = new CreateTagRequest("urgent");

        Result<CreateTagResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("urgent");
        result.Value.CreatedAt.Should().Be(Now);
        result.Value.UpdatedAt.Should().Be(Now);

        saved.Should().NotBeNull();
        saved!.OwnerId.Should().Be(OwnerId);
        saved.TenantId.Should().BeNull("TenantId stays null in V1 (02-data-model.md)");
        result.Value.Id.Should().Be(saved.Id);
    }

    [Fact]
    public async Task HandleAsync_TrimsTheNameBeforeCheckingAndPersisting()
    {
        // "  urgent " and "urgent" are the same tag: the uniqueness check and the
        // persisted row both use the trimmed form.
        Tag? saved = null;
        _tags
            .Setup(t => t.NameExistsAsync(OwnerId, "urgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tags
            .Setup(t => t.AddAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()))
            .Callback<Tag, CancellationToken>((tag, _) => saved = tag)
            .Returns(Task.CompletedTask);

        var request = new CreateTagRequest("  urgent ");

        Result<CreateTagResponse> result =
            await CreateSut().HandleAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("urgent");
        saved!.Name.Should().Be("urgent");
    }
}
