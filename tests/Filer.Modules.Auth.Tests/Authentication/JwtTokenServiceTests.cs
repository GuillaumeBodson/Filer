using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.Modules.Auth.Tests.TestSupport;
using Microsoft.Extensions.Options;
using Xunit;

namespace Filer.Modules.Auth.Tests.Authentication;

public sealed class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 31, 9, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions Options = new()
    {
        Issuer = "filer-tests",
        Audience = "filer-clients",
        SigningKey = "this-is-a-test-signing-key-of-sufficient-length-32+",
        AccessTokenMinutes = 15,
    };

    private static JwtTokenService CreateSut() =>
        new(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock(Now));

    private static JwtSecurityToken Decode(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void CreateAccessToken_IncludesSubjectEmailAndUniqueId()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com" };

        AccessToken issued = CreateSut().CreateAccessToken(user);

        JwtSecurityToken decoded = Decode(issued.Token);
        decoded.Claims.Should().ContainSingle(c => c.Type == AuthClaimTypes.Subject)
            .Which.Value.Should().Be(user.Id.ToString());
        decoded.Claims.Should().ContainSingle(c => c.Type == AuthClaimTypes.Email)
            .Which.Value.Should().Be("user@example.com");
        decoded.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public void CreateAccessToken_SetsConfiguredIssuerAndAudience()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com" };

        JwtSecurityToken decoded = Decode(CreateSut().CreateAccessToken(user).Token);

        decoded.Issuer.Should().Be("filer-tests");
        decoded.Audiences.Should().ContainSingle().Which.Should().Be("filer-clients");
    }

    [Fact]
    public void CreateAccessToken_ExpiresAtClockTimePlusConfiguredLifetime()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com" };

        AccessToken issued = CreateSut().CreateAccessToken(user);

        issued.ExpiresAt.Should().Be(Now.AddMinutes(15));
    }

    [Fact]
    public void CreateAccessToken_WhenUserHasTenant_IncludesTenantClaim()
    {
        Guid tenantId = Guid.NewGuid();
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com", TenantId = tenantId };

        JwtSecurityToken decoded = Decode(CreateSut().CreateAccessToken(user).Token);

        decoded.Claims.Should().ContainSingle(c => c.Type == AuthClaimTypes.TenantId)
            .Which.Value.Should().Be(tenantId.ToString());
    }

    [Fact]
    public void CreateAccessToken_WhenUserHasNoTenant_OmitsTenantClaim()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.com", TenantId = null };

        JwtSecurityToken decoded = Decode(CreateSut().CreateAccessToken(user).Token);

        decoded.Claims.Should().NotContain(c => c.Type == AuthClaimTypes.TenantId);
    }
}
