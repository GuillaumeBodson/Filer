using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Auth.Features.Login;

/// <summary>
/// Plain feature service for login. Verifies credentials via Identity, issues an
/// access token, and mints a refresh token in a fresh rotation family
/// (05-security.md). Invalid credentials return a generic 401 so the API does not
/// reveal whether the email exists.
/// </summary>
public sealed class LoginService(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    IRefreshTokenStore refreshTokenStore,
    IClock clock,
    ILogger<LoginService> logger)
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ITokenService _tokenService = tokenService;
    private readonly IRefreshTokenStore _refreshTokenStore = refreshTokenStore;
    private readonly IClock _clock = clock;
    private readonly ILogger<LoginService> _logger = logger;

    private static readonly Error InvalidCredentials =
        Error.Unauthorized("Invalid email or password.", AuthErrorCodes.InvalidCredentials);

    public async Task<Result<LoginResponse>> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        Result validation = LoginValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<LoginResponse>(validation.Error!);
        }

        ApplicationUser? user = await _userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Unknown email: emit the security signal without an identifier — there is
            // no account to name, and the raw email is PII we redact (05-security.md).
            _logger.LoginFailedUnknownAccount();
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        bool passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            // Known account, wrong password: log the user id so repeated attempts on a
            // real account are visible to brute-force monitoring (05-security.md).
            _logger.LoginFailed(user.Id);
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        AccessToken token = _tokenService.CreateAccessToken(user);
        RefreshTokenMaterial refresh = _tokenService.CreateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            // A login starts a new rotation family; subsequent refreshes stay within it.
            FamilyId = Guid.NewGuid(),
            CreatedAt = _clock.UtcNow,
            ExpiresAt = refresh.ExpiresAt,
        };

        await _refreshTokenStore.AddAsync(refreshToken, ct);
        await _refreshTokenStore.SaveChangesAsync(ct);

        _logger.UserSignedIn(user.Id);
        return Result.Success(new LoginResponse(
            token.Token, token.ExpiresAt, refresh.RawToken, refresh.ExpiresAt));
    }
}

/// <summary>
/// Log messages for <see cref="LoginService"/>, co-located per the house convention
/// (13-code-quality-and-design.md). Identify by user id only — never email or
/// password (05-security.md).
/// </summary>
internal static partial class LoginServiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in.")]
    public static partial void UserSignedIn(this ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed login for user {UserId}: invalid password.")]
    public static partial void LoginFailed(this ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed login for an unknown account.")]
    public static partial void LoginFailedUnknownAccount(this ILogger logger);
}
