using System.Text.Json;
using Filer.ApiClient.Auth;
using Filer.Ui.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Xunit;

namespace Filer.Ui.Tests.Auth;

public sealed class FilerAuthenticationStateProviderTests
{
    private static readonly string[] Roles = ["admin", "user"];

    [Fact]
    public async Task Reports_anonymous_when_no_tokens()
    {
        var store = new FakeTokenStore(initial: null);
        using var provider = new FilerAuthenticationStateProvider(store);

        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Reports_authenticated_user_from_token_claims()
    {
        string jwt = CreateJwt(new Dictionary<string, object>
        {
            ["sub"] = "11111111-1111-1111-1111-111111111111",
            ["email"] = "user@example.com",
        });
        var store = new FakeTokenStore(new TokenPair(jwt, null, "refresh", null));
        using var provider = new FilerAuthenticationStateProvider(store);

        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        state.User.FindFirst("email")!.Value.Should().Be("user@example.com");
        state.User.Identity.Name.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Reports_anonymous_for_a_malformed_token()
    {
        var store = new FakeTokenStore(new TokenPair("not-a-jwt", null, "refresh", null));
        using var provider = new FilerAuthenticationStateProvider(store);

        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Maps_an_array_claim_to_one_claim_per_value()
    {
        string jwt = CreateJwt(new Dictionary<string, object>
        {
            ["email"] = "user@example.com",
            ["role"] = Roles,
        });
        var store = new FakeTokenStore(new TokenPair(jwt, null, "refresh", null));
        using var provider = new FilerAuthenticationStateProvider(store);

        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        state.User.FindAll("role").Select(c => c.Value).Should().BeEquivalentTo("admin", "user");
        state.User.IsInRole("admin").Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Decodes_payloads_regardless_of_base64url_padding(int fillerLength)
    {
        // Varying the payload length by one byte per case walks the encoded length
        // through every mod-4 remainder, covering each re-padding branch.
        string jwt = CreateJwt(new Dictionary<string, object>
        {
            ["email"] = "user@example.com",
            ["filler"] = new string('x', fillerLength),
        });
        var store = new FakeTokenStore(new TokenPair(jwt, null, "refresh", null));
        using var provider = new FilerAuthenticationStateProvider(store);

        AuthenticationState state = await provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        state.User.Identity.Name.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Raises_authentication_state_changed_when_tokens_change()
    {
        var store = new FakeTokenStore(initial: null);
        using var provider = new FilerAuthenticationStateProvider(store);

        var changed = false;
        provider.AuthenticationStateChanged += _ => changed = true;

        await store.SaveAsync(new TokenPair("access", null, "refresh", null), TestContext.Current.CancellationToken);

        changed.Should().BeTrue();
    }

    private static string CreateJwt(Dictionary<string, object> claims)
    {
        static string Segment(object value) =>
            Base64Url(JsonSerializer.SerializeToUtf8Bytes(value));

        string header = Segment(new Dictionary<string, object> { ["alg"] = "none", ["typ"] = "JWT" });
        string payload = Segment(claims);
        return $"{header}.{payload}.";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
