using Filer.SharedKernel.Results;

namespace Filer.Modules.Auth.Features.Register;

/// <summary>
/// Explicit, dependency-free validation of the register request
/// (08-ai-development-guidelines.md: validate requests explicitly).
/// </summary>
public static class RegisterValidator
{
    public static Result Validate(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            !request.Email.Contains('@', StringComparison.Ordinal))
        {
            return Result.Failure(Error.Validation("A valid email is required.", "email"));
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return Result.Failure(Error.Validation("Password must be at least 8 characters.", "password"));
        }

        return Result.Success();
    }
}
