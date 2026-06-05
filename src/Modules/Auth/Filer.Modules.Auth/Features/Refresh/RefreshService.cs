using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Auth.Features.Refresh;

/// <summary>
/// Plain feature service for refresh-token exchange. A valid, unused, unexpired
/// token is rotated: it is consumed and a successor is issued in the same family
/// alongside a new access token. Presenting an already-consumed or revoked token is
/// treated as theft and revokes the whole family. Every rejection returns the same
/// generic 401 so the API never reveals why (05-security.md).
/// </summary>
public sealed class RefreshService(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    IRefreshTokenStore refreshTokenStore,
    IClock clock,
    ILogger<RefreshService> logger)
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ITokenService _tokenService = tokenService;
    private readonly IRefreshTokenStore _refreshTokenStore = refreshTokenStore;
    private readonly IClock _clock = clock;
    private readonly ILogger<RefreshService> _logger = logger;

    private static readonly Error InvalidRefreshToken =
        Error.Unauthorized("The refresh token is invalid.", AuthErrorCodes.InvalidRefreshToken);

    public async Task<Result<RefreshResponse>> HandleAsync(RefreshRequest request, CancellationToken ct)
    {
        Result validation = RefreshValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<RefreshResponse>(validation.Error!);
        }

        string tokenHash = _tokenService.HashRefreshToken(request.RefreshToken);
        RefreshToken? stored = await _refreshTokenStore.FindByHashAsync(tokenHash, ct);
        if (stored is null)
        {
            // No matching token: unknown or already rotated away. Nothing to identify.
            _logger.RefreshRejectedUnknownToken();
            return Result.Failure<RefreshResponse>(InvalidRefreshToken);
        }

        DateTimeOffset now = _clock.UtcNow;

        // A consumed or revoked token presented again is a theft signal: the whole
        // family is compromised, so revoke every token in it (05-security.md).
        if (stored.ConsumedAt is not null || stored.RevokedAt is not null)
        {
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            _logger.RefreshTokenReuseDetected(stored.UserId, stored.FamilyId);
            return Result.Failure<RefreshResponse>(InvalidRefreshToken);
        }

        if (stored.ExpiresAt <= now)
        {
            _logger.RefreshRejectedExpiredToken(stored.UserId);
            return Result.Failure<RefreshResponse>(InvalidRefreshToken);
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
        {
            // The owner no longer exists; the token can never mint a valid access token.
            _logger.RefreshRejectedUnknownUser(stored.UserId);
            return Result.Failure<RefreshResponse>(InvalidRefreshToken);
        }

        // Rotation: consume the presented token and issue a successor in the same family.
        stored.ConsumedAt = now;

        RefreshTokenMaterial rotated = _tokenService.CreateRefreshToken();
        var successor = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = stored.UserId,
            TokenHash = rotated.TokenHash,
            FamilyId = stored.FamilyId,
            CreatedAt = now,
            ExpiresAt = rotated.ExpiresAt,
        };
        await _refreshTokenStore.AddAsync(successor, ct);

        AccessToken access = _tokenService.CreateAccessToken(user);

        await _refreshTokenStore.SaveChangesAsync(ct);

        _logger.RefreshTokenRotated(stored.UserId);
        return Result.Success(new RefreshResponse(
            access.Token, access.ExpiresAt, rotated.RawToken, rotated.ExpiresAt));
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<RefreshToken> family = await _refreshTokenStore.GetFamilyAsync(familyId, ct);
        foreach (RefreshToken token in family)
        {
            token.RevokedAt ??= now;
        }

        await _refreshTokenStore.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Log messages for <see cref="RefreshService"/>, co-located per the house
/// convention (13-code-quality-and-design.md). Identify by user id / family id
/// only — never the token value (05-security.md).
/// </summary>
internal static partial class RefreshServiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Refresh token rotated for user {UserId}.")]
    public static partial void RefreshTokenRotated(this ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh rejected: unknown or already-rotated token.")]
    public static partial void RefreshRejectedUnknownToken(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh rejected: expired token for user {UserId}.")]
    public static partial void RefreshRejectedExpiredToken(this ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh rejected: token owner {UserId} no longer exists.")]
    public static partial void RefreshRejectedUnknownUser(this ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh-token reuse detected for user {UserId}; revoking family {FamilyId}.")]
    public static partial void RefreshTokenReuseDetected(this ILogger logger, Guid userId, Guid familyId);
}
