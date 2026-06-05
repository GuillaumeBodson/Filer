using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Domain;

namespace Filer.Modules.Auth.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IRefreshTokenStore"/> for unit tests: holds tokens in a
/// list and counts saves, so rotation and family-revocation behaviour can be
/// asserted without a database (12-testing-strategy.md).
/// </summary>
internal sealed class FakeRefreshTokenStore : IRefreshTokenStore
{
    public List<RefreshToken> Tokens { get; } = [];

    public int SaveChangesCount { get; private set; }

    public Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Tokens.SingleOrDefault(t => t.TokenHash == tokenHash));

    public Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(Guid familyId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RefreshToken>>(Tokens.Where(t => t.FamilyId == familyId).ToList());

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}
