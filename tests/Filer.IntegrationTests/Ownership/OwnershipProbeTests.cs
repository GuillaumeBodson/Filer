using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Filer.IntegrationTests.Infrastructure;
using Xunit;

namespace Filer.IntegrationTests.Ownership;

/// <summary>
/// Exercises the shared ownership primitive end to end through the real pipeline
/// (JWT validation → ICurrentUser → OwnershipGuard → problem-details) against a
/// test-only owned resource, ahead of the first real owned-resource module. The
/// security-critical guarantee is the same one <see cref="OwnershipTests"/> reserves
/// for the Documents slice: cross-owner access returns <b>404, never 403 or 200</b>
/// (05-security.md, 12-testing-strategy.md).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class OwnershipProbeTests(FilerApiFactory factory)
{
    private const string ProbeResources = "/api/v1/_probe/resources";

    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task CrossOwnerAccess_ToAnotherUsersResource_Returns404()
    {
        HttpClient owner = _factory.CreateClient();
        AuthenticatedUser ownerUser = await owner.RegisterAndAuthenticateAsync();
        owner.WithBearer(ownerUser.AccessToken);
        Guid resourceId = await CreateProbeResourceAsync(owner);

        HttpClient intruder = _factory.CreateClient();
        AuthenticatedUser intruderUser = await intruder.RegisterAndAuthenticateAsync();
        intruder.WithBearer(intruderUser.AccessToken);

        HttpResponseMessage response = await intruder.GetAsync($"{ProbeResources}/{resourceId}", CancellationToken.None);

        // The whole point: not 403 (which would confirm the resource exists), not 200.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OwnerAccess_ToOwnResource_Returns200()
    {
        HttpClient owner = _factory.CreateClient();
        AuthenticatedUser ownerUser = await owner.RegisterAndAuthenticateAsync();
        owner.WithBearer(ownerUser.AccessToken);
        Guid resourceId = await CreateProbeResourceAsync(owner);

        HttpResponseMessage response = await owner.GetAsync($"{ProbeResources}/{resourceId}", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnauthenticatedAccess_ToProbeResource_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"{ProbeResources}/{Guid.NewGuid()}", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NonExistentResource_IsIndistinguishableFromNotOwned_Returns404()
    {
        HttpClient client = _factory.CreateClient();
        AuthenticatedUser user = await client.RegisterAndAuthenticateAsync();
        client.WithBearer(user.AccessToken);

        HttpResponseMessage response = await client.GetAsync($"{ProbeResources}/{Guid.NewGuid()}", CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> CreateProbeResourceAsync(HttpClient client)
    {
        HttpResponseMessage created = await client.PostAsync(ProbeResources, content: null, CancellationToken.None);
        created.StatusCode.Should().Be(HttpStatusCode.OK);

        ProbeResourceResult? resource = await created.Content.ReadFromJsonAsync<ProbeResourceResult>(CancellationToken.None);
        resource.Should().NotBeNull();
        return resource!.Id;
    }

    private sealed record ProbeResourceResult(Guid Id);
}
