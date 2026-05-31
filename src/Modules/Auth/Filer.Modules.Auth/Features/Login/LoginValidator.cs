using Filer.Modules.Auth.Contracts;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Auth.Features.Login;

public static class LoginValidator
{
    public static Result Validate(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Result.Failure(Error.Validation("Email is required.", AuthErrorCodes.Email));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure(Error.Validation("Password is required.", AuthErrorCodes.Password));
        }

        return Result.Success();
    }
}
