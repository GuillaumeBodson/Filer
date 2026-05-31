using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;

namespace Filer.Modules.Auth.Features.Register;

/// <summary>
/// Plain feature service for account creation. The only business entry point for
/// this slice (10-solution-structure.md).
/// </summary>
public sealed class RegisterService(UserManager<ApplicationUser> userManager, IClock clock)
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IClock _clock = clock;

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

            return duplicate
                ? Result.Failure<RegisterResponse>(EmailTaken)
                : Result.Failure<RegisterResponse>(Error.Validation(message, AuthErrorCodes.RegistrationFailed));
        }

        return Result.Success(new RegisterResponse(user.Id, email));
    }
}
