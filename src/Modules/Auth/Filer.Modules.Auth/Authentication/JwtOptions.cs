using System.ComponentModel.DataAnnotations;

namespace Filer.Modules.Auth.Authentication;

/// <summary>
/// JWT configuration bound from the <c>Jwt</c> configuration section. The signing
/// key comes from configuration / secret storage, never source control
/// (05-security.md).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Required]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters.")]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Access-token lifetime; short-lived per 05-security.md (target 15 minutes).</summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; init; } = 15;
}
