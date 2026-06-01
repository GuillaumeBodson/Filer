using System.Net.Http.Json;
using FluentAssertions;
using Filer.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Filer.IntegrationTests.Auth;

/// <summary>
/// Error responses must use the RFC 7807 problem-details shape and must never leak
/// internals such as a stack trace (03-api-specification.md, 05-security.md). Proven
/// here against a real failure rather than asserted in isolation.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ProblemDetailsContractTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task ErrorResponse_UsesProblemDetailsMediaTypeAndShape()
    {
        HttpClient client = _factory.CreateClient();

        // Any handled failure exercises the shared mapping; a bad login is the simplest.
        HttpResponseMessage response = await client.LoginAsync(TestData.UniqueEmail(), TestData.ValidPassword);

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)response.StatusCode);
        problem.Title.Should().NotBeNullOrWhiteSpace();
        problem.Type.Should().StartWith("https://docs/errors/");
    }

    [Fact]
    public async Task ErrorResponse_LeaksNoStackTraceOrExceptionDetail()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.LoginAsync(TestData.UniqueEmail(), TestData.ValidPassword);
        string body = await response.Content.ReadAsStringAsync();

        body.Should().NotContainEquivalentOf("stackTrace");
        body.Should().NotContainEquivalentOf("at Filer.");
        body.Should().NotContainEquivalentOf("Exception");
    }
}
