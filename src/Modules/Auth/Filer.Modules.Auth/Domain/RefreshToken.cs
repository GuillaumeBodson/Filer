namespace Filer.Modules.Auth.Domain;

/// <summary>
/// A long-lived, opaque refresh token, stored server-side as a SHA-256 hash and
/// bound to a user (05-security.md). The raw token is returned to the client once,
/// at issue time, and never persisted — only its <see cref="TokenHash"/> is kept,
/// so a database leak cannot yield a usable token.
///
/// Tokens form a <see cref="FamilyId"/> chain: each refresh consumes the presented
/// token (<see cref="ConsumedAt"/>) and issues a successor in the same family
/// (rotation). Presenting an already-consumed or revoked token is treated as theft
/// and revokes the whole family (05-security.md).
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning user (<see cref="ApplicationUser.Id"/>).</summary>
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hash of the opaque token; the raw value is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Rotation chain this token belongs to; shared by every successor.</summary>
    public Guid FamilyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the token is exchanged (rotated); a consumed token is spent.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Set when the token is revoked (logout or family theft-detection).</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>True only while the token can still be exchanged: live, unspent, unrevoked.</summary>
    public bool IsActiveAt(DateTimeOffset now) =>
        RevokedAt is null && ConsumedAt is null && ExpiresAt > now;
}
