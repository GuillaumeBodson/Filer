using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Features.Create;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Folders.Tests.Features.Create;

/// <summary>Every structural rule of the create request, pinned individually.</summary>
public sealed class CreateFolderValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingOrBlankName_FailsWithNameInvalid(string? name)
    {
        Result result = CreateFolderValidator.Validate(new CreateFolderRequest(name, null));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(FoldersErrorCodes.NameInvalid);
    }

    [Fact]
    public void Validate_NameLongerThanTheCeiling_FailsWithNameInvalid()
    {
        string name = new('a', Folder.MaxNameLength + 1);

        Result result = CreateFolderValidator.Validate(new CreateFolderRequest(name, null));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(FoldersErrorCodes.NameInvalid);
    }

    [Fact]
    public void Validate_NameAtExactlyTheCeiling_Succeeds()
    {
        string name = new('a', Folder.MaxNameLength);

        CreateFolderValidator.Validate(new CreateFolderRequest(name, null))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_NameWithinTheCeilingAfterTrimming_Succeeds()
    {
        // The ceiling applies to the trimmed form the service persists, so
        // padding does not push a valid name over the limit.
        string name = "  " + new string('a', Folder.MaxNameLength) + "  ";

        CreateFolderValidator.Validate(new CreateFolderRequest(name, null))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_OrdinaryName_Succeeds()
    {
        CreateFolderValidator.Validate(new CreateFolderRequest("Invoices", Guid.NewGuid()))
            .IsSuccess.Should().BeTrue();
    }
}
