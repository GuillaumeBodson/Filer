using FluentAssertions;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.Modules.Auth.Features.Register;
using Filer.Modules.Auth.Tests.TestSupport;
using Filer.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Filer.Modules.Auth.Tests.Features.Register;

public sealed class RegisterServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 31, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<UserManager<ApplicationUser>> _userManager = MockUserManager.Create();
    private readonly FixedClock _clock = new(Now);

    private RegisterService CreateSut() => new(_userManager.Object, _clock);

    [Fact]
    public async Task HandleAsync_WhenRequestInvalid_ReturnsValidationErrorWithoutCreatingUser()
    {
        RegisterService sut = CreateSut();

        Result<RegisterResponse> result = await sut.HandleAsync(
            new RegisterRequest("not-an-email", "password123"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        _userManager.Verify(
            m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenIdentityReportsDuplicate_ReturnsConflict()
    {
        _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateUserName",
                Description = "User name 'user@example.com' is already taken.",
            }));
        RegisterService sut = CreateSut();

        Result<RegisterResponse> result = await sut.HandleAsync(
            new RegisterRequest("user@example.com", "password123"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(AuthErrorCodes.EmailTaken);
    }

    [Fact]
    public async Task HandleAsync_WhenIdentityFailsForOtherReason_ReturnsValidationErrorCarryingTheDescription()
    {
        _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequiresDigit",
                Description = "Passwords must have at least one digit.",
            }));
        RegisterService sut = CreateSut();

        Result<RegisterResponse> result = await sut.HandleAsync(
            new RegisterRequest("user@example.com", "password"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(AuthErrorCodes.RegistrationFailed);
        result.Error.Message.Should().Contain("at least one digit");
    }

    [Fact]
    public async Task HandleAsync_WhenCreationSucceeds_ReturnsCreatedUserAndStampsTimestampsFromClock()
    {
        ApplicationUser? created = null;
        _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Callback<ApplicationUser, string>((u, _) => created = u)
            .ReturnsAsync(IdentityResult.Success);
        RegisterService sut = CreateSut();

        Result<RegisterResponse> result = await sut.HandleAsync(
            new RegisterRequest("  user@example.com  ", "password123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("user@example.com");

        created.Should().NotBeNull();
        result.Value.Id.Should().Be(created!.Id);
        created.UserName.Should().Be("user@example.com");
        created.Email.Should().Be("user@example.com");
        created.CreatedAt.Should().Be(Now);
        created.UpdatedAt.Should().Be(Now);
    }
}
