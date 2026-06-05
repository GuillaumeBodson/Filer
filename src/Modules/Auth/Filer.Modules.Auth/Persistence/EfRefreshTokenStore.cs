using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Domain;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Auth.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IRefreshTokenStore"/> over the Auth
/// module's <see cref="AuthDbContext"/>. Returned tokens are change-tracked, so a
/// service can mutate one (e.g. set <c>ConsumedAt</c>) and persist with
/// <see cref="SaveChangesAsync"/>.
/// </summary>
public sealed class EfRefreshTokenStore(AuthDbContext db) : IRefreshTokenStore
{
    private readonly AuthDbContext _db = db;

    public async Task AddAsync(RefreshToken token, CancellationToken ct) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
        _db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(Guid familyId, CancellationToken ct) =>
        await _db.RefreshTokens.Where(t => t.FamilyId == familyId).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
