using FluentAssertions;
using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.Modules.Auth.Features.Login;
using Filer.Modules.Auth.Tests.TestSupport;
using Filer.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Auth.Tests.Features.Login;

public sealed class LoginServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<UserManager<ApplicationUser>> _userManager = MockUserManager.Create();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly FakeRefreshTokenStore _refreshTokenStore = new();

    private LoginService CreateSut() =>
        new(_userManager.Object, _tokenService.Object, _refreshTokenStore, new FixedClock(Now),
            NullLogger<LoginService>.Instance);

    [Fact]
    public async Task HandleAsync_WhenRequestInvalid_ReturnsValidationErrorWithoutTouchingCollaborators()
    {
        LoginService sut = CreateSut();

        Result<LoginResponse> result = await sut.HandleAsync(new LoginRequest("", "pw"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        _userManager.Verify(m => m.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        _tokenService.Verify(t => t.CreateAccessToken(It.IsAny<ApplicationUser>()), Times.Never);
        _refreshTokenStore.Tokens.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenEmailUnknown_ReturnsUnauthorized()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        LoginService sut = CreateSut();

        Result<LoginResponse> result = await sut.HandleAsync(
            new LoginRequest("unknown@example.com", "password123"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be(AuthErrorCodes.InvalidCredentials);
        _tokenService.Verify(t => t.CreateAccessToken(It.IsAny<ApplicationUser>()), Times.Never);
        _refreshTokenStore.Tokens.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenPasswordWrong_ReturnsUnauthorized()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com" };
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(false);
        LoginService sut = CreateSut();

        Result<LoginResponse> result = await sut.HandleAsync(
            new LoginRequest("user@example.com", "wrong-password"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be(AuthErrorCodes.InvalidCredentials);
        _tokenService.Verify(t => t.CreateAccessToken(It.IsAny<ApplicationUser>()), Times.Never);
        _refreshTokenStore.Tokens.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenEmailUnknownAndWhenPasswordWrong_ReturnTheSameError()
    {
        // 05-security.md: login must not reveal whether the email exists. The two
        // failure paths must be indistinguishable to the caller.
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com" };
        var clock = new FixedClock(Now);

        var unknownEmailManager = MockUserManager.Create();
        unknownEmailManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var wrongPasswordManager = MockUserManager.Create();
        wrongPasswordManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        wrongPasswordManager.Setup(m => m.CheckPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(false);

        var request = new LoginRequest("user@example.com", "password123");

        Result<LoginResponse> unknownEmail =
            await new LoginService(unknownEmailManager.Object, _tokenService.Object, new FakeRefreshTokenStore(),
                clock, NullLogger<LoginService>.Instance).HandleAsync(request, CancellationToken.None);
        Result<LoginResponse> wrongPassword =
            await new LoginService(wrongPasswordManager.Object, _tokenService.Object, new FakeRefreshTokenStore(),
                clock, NullLogger<LoginService>.Instance).HandleAsync(request, CancellationToken.None);

        wrongPassword.Error.Should().Be(unknownEmail.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenCredentialsValid_ReturnsAccessAndRefreshTokens()
    {
        var expiresAt = new DateTimeOffset(2026, 1, 1, 12, 15, 0, TimeSpan.Zero);
        var refreshExpiresAt = Now.AddDays(14);
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com" };
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "password123")).ReturnsAsync(true);
        _tokenService.Setup(t => t.CreateAccessToken(user)).Returns(new AccessToken("signed.jwt.token", expiresAt));
        _tokenService.Setup(t => t.CreateRefreshToken())
            .Returns(new RefreshTokenMaterial("raw-refresh-token", "hashed-refresh-token", refreshExpiresAt));
        LoginService sut = CreateSut();

        Result<LoginResponse> result = await sut.HandleAsync(
            new LoginRequest("user@example.com", "password123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("signed.jwt.token");
        result.Value.ExpiresAt.Should().Be(expiresAt);
        result.Value.RefreshToken.Should().Be("raw-refresh-token");
        result.Value.RefreshTokenExpiresAt.Should().Be(refreshExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_WhenCredentialsValid_PersistsHashedRefreshTokenInNewFamily()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com" };
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "password123")).ReturnsAsync(true);
        _tokenService.Setup(t => t.CreateAccessToken(user)).Returns(new AccessToken("token", Now));
        _tokenService.Setup(t => t.CreateRefreshToken())
            .Returns(new RefreshTokenMaterial("raw", "hashed", Now.AddDays(14)));
        LoginService sut = CreateSut();

        await sut.HandleAsync(new LoginRequest("user@example.com", "password123"), CancellationToken.None);

        RefreshToken stored = _refreshTokenStore.Tokens.Should().ContainSingle().Subject;
        stored.TokenHash.Should().Be("hashed");
        stored.UserId.Should().Be(user.Id);
        stored.FamilyId.Should().NotBeEmpty();
        stored.CreatedAt.Should().Be(Now);
        stored.ConsumedAt.Should().BeNull();
        stored.RevokedAt.Should().BeNull();
        _refreshTokenStore.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_TrimsEmailBeforeLookup()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com" };
        _userManager.Setup(m => m.FindByEmailAsync("user@example.com")).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(true);
        _tokenService.Setup(t => t.CreateAccessToken(user)).Returns(new AccessToken("token", Now));
        _tokenService.Setup(t => t.CreateRefreshToken())
            .Returns(new RefreshTokenMaterial("raw", "hashed", Now.AddDays(14)));
        LoginService sut = CreateSut();

        Result<LoginResponse> result = await sut.HandleAsync(
            new LoginRequest("  user@example.com  ", "password123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userManager.Verify(m => m.FindByEmailAsync("user@example.com"), Times.Once);
    }
}
