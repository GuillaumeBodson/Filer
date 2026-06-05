using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Filer.IntegrationTests.Infrastructure;

/// <summary>
/// Thin helpers over <see cref="HttpClient"/> for the common arrange steps —
/// registering an account and obtaining a bearer token — so individual tests read
/// as the behaviour they assert, not plumbing.
/// </summary>
public static class ApiClientExtensions
{
    public static Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client, TestData.RegisterRequest request) =>
        client.PostAsJsonAsync("/api/v1/auth/register", request);

    public static Task<HttpResponseMessage> LoginAsync(
        this HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new TestData.LoginRequest(email, password));

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new TestData.RefreshRequest(refreshToken));

    public static Task<HttpResponseMessage> LogoutAsync(this HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/logout", new TestData.LogoutRequest(refreshToken));

    /// <summary>
    /// Registers a fresh account and returns its credentials and a valid token —
    /// the standard arrange step for tests that need an authenticated caller.
    /// </summary>
    public static async Task<AuthenticatedUser> RegisterAndAuthenticateAsync(this HttpClient client)
    {
        TestData.RegisterRequest registration = TestData.NewRegistration();

        HttpResponseMessage register = await client.RegisterAsync(registration);
        register.EnsureSuccessStatusCode();
        RegisterResult created = (await register.Content.ReadFromJsonAsync<RegisterResult>())!;

        HttpResponseMessage login = await client.LoginAsync(registration.Email, registration.Password);
        login.EnsureSuccessStatusCode();
        LoginResult token = (await login.Content.ReadFromJsonAsync<LoginResult>())!;

        return new AuthenticatedUser(created.Id, registration.Email, token.AccessToken);
    }

    /// <summary>Attaches a bearer token to outgoing requests.</summary>
    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>A registered account and the token issued for it.</summary>
public sealed record AuthenticatedUser(Guid Id, string Email, string AccessToken);
