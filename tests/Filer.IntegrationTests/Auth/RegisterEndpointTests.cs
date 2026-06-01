using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Filer.IntegrationTests.Infrastructure;
using Filer.Modules.Auth.Contracts;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Filer.IntegrationTests.Auth;

/// <summary>
/// POST /api/v1/auth/register, exercised end to end through the real HTTP pipeline
/// and a real Postgres. Covers the happy path plus each externally observable
/// failure of the slice (12-testing-strategy.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RegisterEndpointTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task Register_WithValidPayload_Returns201WithProfile()
    {
        HttpClient client = _factory.CreateClient();
        TestData.RegisterRequest request = TestData.NewRegistration();

        HttpResponseMessage response = await client.RegisterAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        RegisterResult? body = await response.Content.ReadFromJsonAsync<RegisterResult>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBe(Guid.Empty);
        body.Email.Should().Be(request.Email);

        response.Headers.Location!.ToString()
            .Should().Be($"/api/v1/auth/users/{body.Id}");
    }

    [Fact]
    public async Task Register_WhenEmailAlreadyRegistered_Returns409EmailTaken()
    {
        HttpClient client = _factory.CreateClient();
        TestData.RegisterRequest request = TestData.NewRegistration();

        (await client.RegisterAsync(request)).StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage duplicate = await client.RegisterAsync(request);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemDetails? problem = await duplicate.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be(AuthErrorCodes.EmailTaken);
    }

    [Fact]
    public async Task Register_WithEmailMissingAtSign_Returns400EmailValidation()
    {
        HttpClient client = _factory.CreateClient();
        var request = new TestData.RegisterRequest("not-an-email", TestData.ValidPassword);

        HttpResponseMessage response = await client.RegisterAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be(AuthErrorCodes.Email);
    }

    [Fact]
    public async Task Register_WithShortPassword_Returns400PasswordValidation()
    {
        HttpClient client = _factory.CreateClient();
        var request = new TestData.RegisterRequest(TestData.UniqueEmail(), "short");

        HttpResponseMessage response = await client.RegisterAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.Should().Be(AuthErrorCodes.Password);
    }
}
