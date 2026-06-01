using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Filer.IntegrationTests.Infrastructure;
using Xunit;

namespace Filer.IntegrationTests.Auth;

/// <summary>
/// The walking-skeleton smoke path (README: register → login → me) as an
/// executable guarantee: the identity minted at registration is the same one the
/// token carries and <c>/me</c> reports back, across the real host and Postgres.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RegisterLoginMeFlowTests(FilerApiFactory factory)
{
    private readonly FilerApiFactory _factory = factory;

    [Fact]
    public async Task RegisterThenLoginThenMe_SurfacesOneConsistentIdentity()
    {
        HttpClient client = _factory.CreateClient();
        TestData.RegisterRequest account = TestData.NewRegistration();

        HttpResponseMessage register = await client.RegisterAsync(account);
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        RegisterResult registered = (await register.Content.ReadFromJsonAsync<RegisterResult>())!;

        HttpResponseMessage login = await client.LoginAsync(account.Email, account.Password);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        LoginResult token = (await login.Content.ReadFromJsonAsync<LoginResult>())!;

        client.WithBearer(token.AccessToken);
        HttpResponseMessage me = await client.GetAsync("/api/v1/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        MeResult profile = (await me.Content.ReadFromJsonAsync<MeResult>())!;

        profile.Id.Should().Be(registered.Id);
        profile.Email.Should().Be(account.Email);
    }
}
