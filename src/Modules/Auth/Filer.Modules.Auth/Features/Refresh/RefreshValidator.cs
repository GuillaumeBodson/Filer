using Filer.Modules.Auth.Contracts;
using Filer.SharedKernel.Results;

namespace Filer.Modules.Auth.Features.Refresh;

public static class RefreshValidator
{
    public static Result Validate(RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Failure(Error.Validation("Refresh token is required.", AuthErrorCodes.RefreshToken));
        }

        return Result.Success();
    }
}
