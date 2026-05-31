using FluentAssertions;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Features.Register;
using Filer.SharedKernel.Results;
using Xunit;

namespace Filer.Modules.Auth.Tests.Features.Register;

public sealed class RegisterValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("no-at-sign.com")]
    public void Validate_WhenEmailInvalid_ReturnsEmailValidationError(string? email)
    {
        Result result = RegisterValidator.Validate(new RegisterRequest(email!, "password123"));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(AuthErrorCodes.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("short")]    // 5 chars
    [InlineData("1234567")]  // 7 chars, just under the minimum
    public void Validate_WhenPasswordTooShort_ReturnsPasswordValidationError(string? password)
    {
        Result result = RegisterValidator.Validate(new RegisterRequest("user@example.com", password!));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(AuthErrorCodes.Password);
    }

    [Fact]
    public void Validate_WhenEmailValidAndPasswordLongEnough_ReturnsSuccess()
    {
        Result result = RegisterValidator.Validate(new RegisterRequest("user@example.com", "12345678"));

        result.IsSuccess.Should().BeTrue();
    }
}
