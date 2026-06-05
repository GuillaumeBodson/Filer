using Filer.Modules.Auth.Contracts;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Auth.Features.Logout;

public static class LogoutValidator
{
    public static Result Validate(LogoutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Failure(Error.Validation("Refresh token is required.", AuthErrorCodes.RefreshToken));
        }

        return Result.Success();
    }
}
