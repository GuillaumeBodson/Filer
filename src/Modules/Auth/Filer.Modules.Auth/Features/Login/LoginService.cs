using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;

namespace Filer.Modules.Auth.Features.Login;

/// <summary>
/// Plain feature service for login. Verifies credentials via Identity and issues
/// an access token. Invalid credentials return a generic 401 so the API does not
/// reveal whether the email exists (05-security.md).
/// </summary>
public sealed class LoginService(UserManager<ApplicationUser> userManager, ITokenService tokenService)
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ITokenService _tokenService = tokenService;

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
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        bool passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        AccessToken token = _tokenService.CreateAccessToken(user);
        return Result.Success(new LoginResponse(token.Token, token.ExpiresAt));
    }
}
