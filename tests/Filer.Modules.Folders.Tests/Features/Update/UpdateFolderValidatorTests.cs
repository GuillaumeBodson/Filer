using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Features.Update;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Folders.Tests.Features.Update;

/// <summary>
/// Pins the structural rules of the rename/move patch: at least one field, and a
/// present name must be non-blank and within <see cref="Folder.MaxNameLength"/>.
/// Requests are built through the property setters, which is exactly how the
/// serializer sets the <c>Has*</c> presence flags (UpdateFolderRequestTests pins
/// that contract).
/// </summary>
public sealed class UpdateFolderValidatorTests
{
    [Fact]
    public void Validate_WhenThePatchTouchesNothing_FailsWithUpdateEmpty()
    {
        Result result = UpdateFolderValidator.Validate(new UpdateFolderRequest());

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(FoldersErrorCodes.UpdateEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenThePresentNameIsMissingOrBlank_FailsWithNameInvalid(string? name)
    {
        var request = new UpdateFolderRequest { Name = name };

        Result result = UpdateFolderValidator.Validate(request);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(FoldersErrorCodes.NameInvalid);
    }

    [Fact]
    public void Validate_WhenTheNameExceedsTheCeiling_FailsWithNameInvalid()
    {
        var request = new UpdateFolderRequest { Name = new string('a', Folder.MaxNameLength + 1) };

        Result result = UpdateFolderValidator.Validate(request);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(FoldersErrorCodes.NameInvalid);
    }

    [Fact]
    public void Validate_AcceptsANameAtExactlyTheCeiling()
    {
        var request = new UpdateFolderRequest { Name = new string('a', Folder.MaxNameLength) };

        Result result = UpdateFolderValidator.Validate(request);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_AcceptsARenameOnlyPatch()
    {
        Result result = UpdateFolderValidator.Validate(new UpdateFolderRequest { Name = "Inbox" });

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0c5538fc-9874-4f6a-8d28-49d8059facf0")]
    public void Validate_AcceptsAMoveOnlyPatch(string? parentId)
    {
        // Explicit null is a valid move (to the top level), not a missing value.
        var request = new UpdateFolderRequest
        {
            ParentId = parentId is null ? null : Guid.Parse(parentId),
        };

        Result result = UpdateFolderValidator.Validate(request);

        result.IsSuccess.Should().BeTrue();
    }
}
