using FluentAssertions;
using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.Modules.Auth.Features.Refresh;
using Filer.Modules.Auth.Tests.TestSupport;
using Filer.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Auth.Tests.Features.Refresh;

public sealed class RefreshServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);

    private const string RawToken = "raw-refresh-token";
    private const string TokenHash = "hashed-refresh-token";

    private readonly Mock<UserManager<ApplicationUser>> _userManager = MockUserManager.Create();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly FakeRefreshTokenStore _store = new();

    public RefreshServiceTests()
    {
        // The service hashes the presented token to look it up; pin that mapping.
        _tokenService.Setup(t => t.HashRefreshToken(RawToken)).Returns(TokenHash);
    }

    private RefreshService CreateSut() =>
        new(_userManager.Object, _tokenService.Object, _store, new FixedClock(Now),
            NullLogger<RefreshService>.Instance);

    private RefreshToken SeedActiveToken(Guid? userId = null, Guid? familyId = null)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            TokenHash = TokenHash,
            FamilyId = familyId ?? Guid.NewGuid(),
            CreatedAt = Now.AddMinutes(-5),
            ExpiresAt = Now.AddDays(14),
        };
        _store.Tokens.Add(token);
        return token;
    }

    private void SetupKnownUser(Guid userId) =>
        _userManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(new ApplicationUser { Id = userId, Email = "user@example.com" });

    [Fact]
    public async Task HandleAsync_WhenTokenMissing_ReturnsValidationError()
    {
        Result<RefreshResponse> result = await CreateSut().HandleAsync(new RefreshRequest(" "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(AuthErrorCodes.RefreshToken);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenUnknown_ReturnsUnauthorized()
    {
        Result<RefreshResponse> result =
            await CreateSut().HandleAsync(new RefreshRequest(RawToken), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be(AuthErrorCodes.InvalidRefreshToken);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenExpired_ReturnsUnauthorizedWithoutRotating()
    {
        RefreshToken token = SeedActiveToken();
        token.ExpiresAt = Now.AddSeconds(-1);

        Result<RefreshResponse> result =
            await CreateSut().HandleAsync(new RefreshRequest(RawToken), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(AuthErrorCodes.InvalidRefreshToken);
        _store.Tokens.Should().ContainSingle();
        token.ConsumedAt.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenTokenOwnerNoLongerExists_ReturnsUnauthorized()
    {
        RefreshToken token = SeedActiveToken();
        _userManager.Setup(m => m.FindByIdAsync(token.UserId.ToString())).ReturnsAsync((ApplicationUser?)null);

        Result<RefreshResponse> result =
            await CreateSut().HandleAsync(new RefreshRequest(RawToken), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(AuthErrorCodes.InvalidRefreshToken);
        token.ConsumedAt.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenTokenValid_RotatesAndReturnsNewPair()
    {
        var userId = Guid.NewGuid();
        Guid familyId = Guid.NewGuid();
        RefreshToken original = SeedActiveToken(userId, familyId);
        SetupKnownUser(userId);

        var accessExpiry = Now.AddMinutes(15);
        var refreshExpiry = Now.AddDays(14);
        _tokenService.Setup(t => t.CreateAccessToken(It.Is<ApplicationUser>(u => u.Id == userId)))
            .Returns(new AccessToken("new.access.token", accessExpiry));
        _tokenService.Setup(t => t.CreateRefreshToken())
            .Returns(new RefreshTokenMaterial("new-raw", "new-hash", refreshExpiry));

        Result<RefreshResponse> result =
            await CreateSut().HandleAsync(new RefreshRequest(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("new.access.token");
        result.Value.ExpiresAt.Should().Be(accessExpiry);
        result.Value.RefreshToken.Should().Be("new-raw");
        result.Value.RefreshTokenExpiresAt.Should().Be(refreshExpiry);

        // The presented token is consumed (rotation), not deleted.
        original.ConsumedAt.Should().Be(Now);
        original.RevokedAt.Should().BeNull();

        // A successor is stored in the same family.
        RefreshToken successor = _store.Tokens.Single(t => t.TokenHash == "new-hash");
        successor.FamilyId.Should().Be(familyId);
        successor.UserId.Should().Be(userId);
        successor.CreatedAt.Should().Be(Now);
        _store.SaveChangesCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HandleAsync_WhenConsumedTokenReused_RevokesWholeFamilyAndRejects()
    {
        var userId = Guid.NewGuid();
        Guid familyId = Guid.NewGuid();

        // The reused token: already consumed by an earlier rotation.
        RefreshToken reused = SeedActiveToken(userId, familyId);
        reused.ConsumedAt = Now.AddMinutes(-1);

        // Its legitimate successor, still active — must be revoked by theft-detection.
        var successor = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = "successor-hash",
            FamilyId = familyId,
            CreatedAt = Now.AddMinutes(-1),
            ExpiresAt = Now.AddDays(14),
        };
        _store.Tokens.Add(successor);

        Result<RefreshResponse> result =
            await CreateSut().HandleAsync(new RefreshRequest(RawToken), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be(AuthErrorCodes.InvalidRefreshToken);

        // Every token in the family is now revoked — theft response (05-security.md).
        _store.Tokens.Where(t => t.FamilyId == familyId).Should().OnlyContain(t => t.RevokedAt == Now);
        // No new token issued.
        _store.Tokens.Should().NotContain(t => t.ConsumedAt == null && t.RevokedAt == null);
    }

    [Fact]
    public async Task HandleAsync_WhenRevokedTokenPresented_ReturnsUnauthorized()
    {
        RefreshToken token = SeedActiveToken();
        token.RevokedAt = Now.AddMinutes(-1);

        Result<RefreshResponse> result =
            await CreateSut().HandleAsync(new RefreshRequest(RawToken), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(AuthErrorCodes.InvalidRefreshToken);
        _tokenService.Verify(t => t.CreateAccessToken(It.IsAny<ApplicationUser>()), Times.Never);
    }
}
