using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Domain;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Auth.Features.Logout;

/// <summary>
/// Plain feature service for logout. The authenticated caller presents a refresh
/// token; the whole rotation family it belongs to is revoked so neither the
/// presented token nor any live successor can mint another access token
/// (05-security.md). The operation is deliberately idempotent and never reveals
/// whether a token existed: an unknown token, or one owned by a different user, is
/// a silent no-op success. Only revocation of the caller's own family has an
/// effect. Access tokens are stateless and expire on their own short lifetime.
/// </summary>
public sealed class LogoutService(
    IRefreshTokenStore refreshTokenStore,
    ITokenService tokenService,
    IClock clock,
    ILogger<LogoutService> logger)
{
    private readonly IRefreshTokenStore _refreshTokenStore = refreshTokenStore;
    private readonly ITokenService _tokenService = tokenService;
    private readonly IClock _clock = clock;
    private readonly ILogger<LogoutService> _logger = logger;

    public async Task<Result> HandleAsync(Guid userId, LogoutRequest request, CancellationToken ct)
    {
        Result validation = LogoutValidator.Validate(request);
        if (validation.IsFailure)
        {
            return validation;
        }

        string tokenHash = _tokenService.HashRefreshToken(request.RefreshToken);
        RefreshToken? stored = await _refreshTokenStore.FindByHashAsync(tokenHash, ct);

        // Unknown token: already gone, or never existed. Logout is idempotent —
        // the desired end state (no usable token) already holds, so succeed.
        if (stored is null)
        {
            _logger.LogoutTokenNotFound(userId);
            return Result.Success();
        }

        // A token the caller does not own must never be revoked by them, and its
        // existence must not be revealed. Treat it as a no-op success (05-security.md).
        if (stored.UserId != userId)
        {
            _logger.LogoutOwnershipMismatch(userId);
            return Result.Success();
        }

        DateTimeOffset now = _clock.UtcNow;
        await RevokeFamilyAsync(stored.FamilyId, now, ct);

        _logger.LoggedOut(userId, stored.FamilyId);
        return Result.Success();
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
/// Log messages for <see cref="LogoutService"/>, co-located per the house
/// convention (13-code-quality-and-design.md). Identify by user id / family id
/// only — never the token value (05-security.md).
/// </summary>
internal static partial class LogoutServiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged out; revoked refresh-token family {FamilyId}.")]
    public static partial void LoggedOut(this ILogger logger, Guid userId, Guid familyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Logout by user {UserId}: presented refresh token not found; no-op.")]
    public static partial void LogoutTokenNotFound(this ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Logout by user {UserId}: presented refresh token belongs to another user; no-op.")]
    public static partial void LogoutOwnershipMismatch(this ILogger logger, Guid userId);
}
