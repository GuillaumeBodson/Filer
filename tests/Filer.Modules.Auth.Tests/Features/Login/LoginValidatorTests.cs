using FluentAssertions;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Features.Login;
using Filer.SharedKernel.Results;
using Xunit;

namespace Filer.Modules.Auth.Tests.Features.Login;

public sealed class LoginValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WhenEmailMissing_ReturnsEmailValidationError(string? email)
    {
        Result result = LoginValidator.Validate(new LoginRequest(email!, "any-password"));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(AuthErrorCodes.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WhenPasswordMissing_ReturnsPasswordValidationError(string? password)
    {
        Result result = LoginValidator.Validate(new LoginRequest("user@example.com", password!));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(AuthErrorCodes.Password);
    }

    [Fact]
    public void Validate_WhenEmailAndPasswordPresent_ReturnsSuccess()
    {
        Result result = LoginValidator.Validate(new LoginRequest("user@example.com", "password123"));

        result.IsSuccess.Should().BeTrue();
    }
}
