namespace Filer.Modules.Auth.Contracts;

/// <summary>
/// Stable error codes returned by the Auth module. They are part of the public API
/// contract (03-api-specification.md): clients key off them and the host emits each
/// as the problem <c>title</c> and the <c>https://docs/errors/{code}</c> type.
/// Centralised here — like <see cref="AuthClaimTypes"/> — so a typo can never silently
/// break the contract.
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>The email field failed validation.</summary>
    public const string Email = "email";

    /// <summary>The password field failed validation.</summary>
    public const string Password = "password";

    /// <summary>An account already exists for the supplied email.</summary>
    public const string EmailTaken = "email_taken";

    /// <summary>Account creation failed for a reason other than a duplicate email.</summary>
    public const string RegistrationFailed = "registration_failed";

    /// <summary>The supplied credentials did not match an account.</summary>
    public const string InvalidCredentials = "invalid_credentials";

    /// <summary>The refresh-token field was missing or empty.</summary>
    public const string RefreshToken = "refresh_token";

    /// <summary>
    /// The presented refresh token was unknown, expired, already used, or revoked.
    /// Deliberately generic so the API never distinguishes the cause (05-security.md).
    /// </summary>
    public const string InvalidRefreshToken = "invalid_refresh_token";
}
