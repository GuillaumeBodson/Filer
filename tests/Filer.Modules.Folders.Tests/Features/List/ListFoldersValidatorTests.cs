using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Features.List;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Folders.Tests.Features.List;

/// <summary>
/// Pins the view-parameter contract of <c>GET /api/v1/folders</c>
/// (03-api-specification.md): <c>flat</c> and <c>tree</c> parse
/// case-insensitively, absence means <c>flat</c>, anything else is the 400.
/// </summary>
public sealed class ListFoldersValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithoutAView_DefaultsToFlat(string? view)
    {
        Result<FolderListView> result = ListFoldersValidator.Validate(new ListFoldersQuery(view));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(FolderListView.Flat);
    }

    // The expected FolderListView is internal, so the two theories stay separate
    // instead of taking it as a (public) test-method parameter.
    [Theory]
    [InlineData("flat")]
    [InlineData("FLAT")]
    [InlineData(" flat ")]
    public void Validate_WithTheFlatView_ParsesCaseInsensitively(string view)
    {
        Result<FolderListView> result = ListFoldersValidator.Validate(new ListFoldersQuery(view));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(FolderListView.Flat);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("Tree")]
    [InlineData(" TREE ")]
    public void Validate_WithTheTreeView_ParsesCaseInsensitively(string view)
    {
        Result<FolderListView> result = ListFoldersValidator.Validate(new ListFoldersQuery(view));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(FolderListView.Tree);
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("flat,tree")]
    [InlineData("0")]
    public void Validate_WithAnUnknownView_FailsWithViewInvalid(string view)
    {
        Result<FolderListView> result = ListFoldersValidator.Validate(new ListFoldersQuery(view));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(FoldersErrorCodes.ViewInvalid);
    }
}
