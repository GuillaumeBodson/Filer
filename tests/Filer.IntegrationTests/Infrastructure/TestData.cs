namespace Filer.IntegrationTests.Infrastructure;

/// <summary>
/// Intention-revealing builders for request payloads (12-testing-strategy.md:
/// explicit factories over shared mutable fixtures). Emails are unique per call so
/// tests sharing one database stay isolated without resetting it between runs.
/// </summary>
public static class TestData
{
    public const string ValidPassword = "Sup3rSecret!";

    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@filer.test";

    public static RegisterRequest NewRegistration(string? email = null, string? password = null) =>
        new(email ?? UniqueEmail(), password ?? ValidPassword);

    // Request DTOs mirror the API contract (03-api-specification.md). Declared here
    // rather than referenced from the module so a contract drift surfaces as a
    // failing integration test, not a silently-recompiled shape.
    public sealed record RegisterRequest(string Email, string Password);

    public sealed record LoginRequest(string Email, string Password);

    public sealed record RefreshRequest(string RefreshToken);
}

/// <summary>Response shapes asserted by tests, matching the API's JSON output.</summary>
public sealed record RegisterResult(Guid Id, string Email);

public sealed record LoginResult(
    string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt, string TokenType);

public sealed record RefreshResult(
    string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt, string TokenType);

public sealed record MeResult(Guid Id, string Email);
