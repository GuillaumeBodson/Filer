using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Features.Create;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Tags.Tests.Features.Create;

/// <summary>Every structural rule of the create request, pinned individually.</summary>
public sealed class CreateTagValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingOrBlankName_FailsWithNameInvalid(string? name)
    {
        Result result = CreateTagValidator.Validate(new CreateTagRequest(name));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(TagsErrorCodes.NameInvalid);
    }

    [Fact]
    public void Validate_NameLongerThanTheCeiling_FailsWithNameInvalid()
    {
        string name = new('a', Tag.MaxNameLength + 1);

        Result result = CreateTagValidator.Validate(new CreateTagRequest(name));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(TagsErrorCodes.NameInvalid);
    }

    [Fact]
    public void Validate_NameAtExactlyTheCeiling_Succeeds()
    {
        string name = new('a', Tag.MaxNameLength);

        CreateTagValidator.Validate(new CreateTagRequest(name))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_NameWithinTheCeilingAfterTrimming_Succeeds()
    {
        // The ceiling applies to the trimmed form the service persists, so
        // padding does not push a valid name over the limit.
        string name = "  " + new string('a', Tag.MaxNameLength) + "  ";

        CreateTagValidator.Validate(new CreateTagRequest(name))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_OrdinaryName_Succeeds()
    {
        CreateTagValidator.Validate(new CreateTagRequest("urgent"))
            .IsSuccess.Should().BeTrue();
    }
}
