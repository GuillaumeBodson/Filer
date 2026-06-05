using FluentAssertions;
using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.Modules.Auth.Features.Logout;
using Filer.Modules.Auth.Tests.TestSupport;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Filer.Modules.Auth.Tests.Features.Logout;

/// <summary>
/// LogoutService revokes the caller's whole refresh-token family. It is idempotent
/// and leak-free: an empty token is a 400, but an unknown token or one owned by
/// another user is a silent no-op success that never reveals whether the token
/// existed (05-security.md).
/// </summary>
public sealed class LogoutServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);

    private const string RawToken = "raw-refresh-token";
    private const string TokenHash = "hashed-refresh-token";

    private readonly Mock<ITokenService> _tokenService = new();
    private readonly FakeRefreshTokenStore _store = new();

    public LogoutServiceTests()
    {
        // The service hashes the presented token to look it up; pin that mapping.
        _tokenService.Setup(t => t.HashRefreshToken(RawToken)).Returns(TokenHash);
    }

    private LogoutService CreateSut() =>
        new(_store, _tokenService.Object, new FixedClock(Now), NullLogger<LogoutService>.Instance);

    private RefreshToken SeedActiveToken(Guid userId, Guid? familyId = null, string hash = TokenHash)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            FamilyId = familyId ?? Guid.NewGuid(),
            CreatedAt = Now.AddMinutes(-5),
            ExpiresAt = Now.AddDays(14),
        };
        _store.Tokens.Add(token);
        return token;
    }

    [Fact]
    public async Task HandleAsync_WhenTokenMissing_ReturnsValidationError()
    {
        Result result = await CreateSut().HandleAsync(Guid.NewGuid(), new LogoutRequest(" "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(AuthErrorCodes.RefreshToken);
        _store.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenUnknown_SucceedsAsNoOp()
    {
        // Idempotent: nothing to revoke, but the desired end state already holds.
        Result result =
            await CreateSut().HandleAsync(Guid.NewGuid(), new LogoutRequest(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _store.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenOwnedByAnotherUser_SucceedsWithoutRevoking()
    {
        Guid otherUser = Guid.NewGuid();
        RefreshToken token = SeedActiveToken(otherUser);

        Result result =
            await CreateSut().HandleAsync(Guid.NewGuid(), new LogoutRequest(RawToken), CancellationToken.None);

        // No-op success: the other user's token is untouched and its existence hidden.
        result.IsSuccess.Should().BeTrue();
        token.RevokedAt.Should().BeNull();
        _store.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenValid_RevokesWholeFamily()
    {
        Guid userId = Guid.NewGuid();
        Guid familyId = Guid.NewGuid();
        RefreshToken presented = SeedActiveToken(userId, familyId);

        // A live successor in the same family must also die on logout.
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

        Result result =
            await CreateSut().HandleAsync(userId, new LogoutRequest(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _store.Tokens.Where(t => t.FamilyId == familyId).Should().OnlyContain(t => t.RevokedAt == Now);
        presented.RevokedAt.Should().Be(Now);
        successor.RevokedAt.Should().Be(Now);
        _store.SaveChangesCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenAlreadyRevoked_PreservesOriginalRevocationInstant()
    {
        Guid userId = Guid.NewGuid();
        RefreshToken token = SeedActiveToken(userId);
        DateTimeOffset earlier = Now.AddMinutes(-30);
        token.RevokedAt = earlier;

        Result result =
            await CreateSut().HandleAsync(userId, new LogoutRequest(RawToken), CancellationToken.None);

        // Idempotent logout does not overwrite an existing revocation timestamp.
        result.IsSuccess.Should().BeTrue();
        token.RevokedAt.Should().Be(earlier);
    }
}
