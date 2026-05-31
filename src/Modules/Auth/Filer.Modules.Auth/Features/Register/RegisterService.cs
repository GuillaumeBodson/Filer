using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Auth.Features.Register;

/// <summary>
/// Plain feature service for account creation. The only business entry point for
/// this slice (10-solution-structure.md).
/// </summary>
public sealed class RegisterService(
    UserManager<ApplicationUser> userManager,
    IClock clock,
    ILogger<RegisterService> logger)
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IClock _clock = clock;
    private readonly ILogger<RegisterService> _logger = logger;

    // ASP.NET Identity prefixes duplicate-account error codes with "Duplicate"
    // (e.g. DuplicateUserName, DuplicateEmail); we treat any of them as email-taken.
    private const string DuplicateIdentityErrorPrefix = "Duplicate";

    private static readonly Error EmailTaken =
        Error.Conflict("An account with this email already exists.", AuthErrorCodes.EmailTaken);

    public async Task<Result<RegisterResponse>> HandleAsync(RegisterRequest request, CancellationToken ct)
    {
        Result validation = RegisterValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<RegisterResponse>(validation.Error!);
        }

        string email = request.Email.Trim();
        DateTimeOffset now = _clock.UtcNow;

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            CreatedAt = now,
            UpdatedAt = now,
        };

        IdentityResult result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            bool duplicate = result.Errors.Any(e =>
                e.Code.Contains(DuplicateIdentityErrorPrefix, StringComparison.OrdinalIgnoreCase));

            string message = string.Join(" ", result.Errors.Select(e => e.Description));

            if (!duplicate)
            {
                // Unexpected rejection (not a taken email): a handled degradation worth
                // a trace. Log Identity's error codes only — never the descriptions,
                // which can echo submitted input, nor the password (05-security.md).
                _logger.RegistrationRejected(string.Join(",", result.Errors.Select(e => e.Code)));
            }

            return duplicate
                ? Result.Failure<RegisterResponse>(EmailTaken)
                : Result.Failure<RegisterResponse>(Error.Validation(message, AuthErrorCodes.RegistrationFailed));
        }

        _logger.UserRegistered(user.Id);
        return Result.Success(new RegisterResponse(user.Id, email));
    }
}

/// <summary>
/// Log messages for <see cref="RegisterService"/>, co-located per the house
/// convention (13-code-quality-and-design.md). Identify by user id only — never
/// email or password (05-security.md).
/// </summary>
internal static partial class RegisterServiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered.")]
    public static partial void UserRegistered(this ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registration rejected by Identity: {IdentityErrorCodes}.")]
    public static partial void RegistrationRejected(this ILogger logger, string identityErrorCodes);
}
