using FluentAssertions;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Features.Refresh;
using Filer.SharedKernel.Results;
using Xunit;

namespace Filer.Modules.Auth.Tests.Features.Refresh;

public sealed class RefreshValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenRefreshTokenBlank_FailsWithRefreshTokenCode(string token)
    {
        Result result = RefreshValidator.Validate(new RefreshRequest(token));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(AuthErrorCodes.RefreshToken);
    }

    [Fact]
    public void Validate_WhenRefreshTokenPresent_Succeeds()
    {
        RefreshValidator.Validate(new RefreshRequest("a-token")).IsSuccess.Should().BeTrue();
    }
}
