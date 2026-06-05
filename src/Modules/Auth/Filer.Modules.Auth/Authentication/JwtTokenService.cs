using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Filer.Modules.Auth.Contracts;
using Filer.Modules.Auth.Domain;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Filer.Modules.Auth.Authentication;

/// <summary>JWT implementation of <see cref="ITokenService"/>.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    // 256 bits of entropy: collision- and guess-resistant, the opaque-token bar in 05-security.md.
    private const int RefreshTokenBytes = 32;

    private readonly JwtOptions _options = options.Value;
    private readonly IClock _clock = clock;

    public AccessToken CreateAccessToken(ApplicationUser user)
    {
        DateTimeOffset issuedAt = _clock.UtcNow;
        DateTimeOffset expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AuthClaimTypes.Subject, user.Id.ToString()),
            new(AuthClaimTypes.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (user.TenantId is { } tenantId)
        {
            claims.Add(new Claim(AuthClaimTypes.TenantId, tenantId.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        string encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(encoded, expiresAt);
    }

    public RefreshTokenMaterial CreateRefreshToken()
    {
        // Opaque, cryptographically random; URL-safe so it survives transport intact.
        string rawToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        DateTimeOffset expiresAt = _clock.UtcNow.AddDays(_options.RefreshTokenDays);
        return new RefreshTokenMaterial(rawToken, HashRefreshToken(rawToken), expiresAt);
    }

    public string HashRefreshToken(string rawToken)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }
}
