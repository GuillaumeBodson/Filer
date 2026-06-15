using Filer.ApiClient.Auth;

namespace Filer.Ui.Tests.Auth;

/// <summary>In-memory <see cref="ITokenStore"/> for tests; records save/clear counts.</summary>
internal sealed class FakeTokenStore : ITokenStore
{
    private TokenPair? _tokens;

    public FakeTokenStore(TokenPair? initial = null) => _tokens = initial;

    public event EventHandler? Changed;

    public int SaveCount { get; private set; }
    public int ClearCount { get; private set; }
    public int ChangedCount { get; private set; }

    public TokenPair? Current => _tokens;

    public Task<TokenPair?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_tokens);

    public Task SaveAsync(TokenPair tokens, CancellationToken cancellationToken = default)
    {
        _tokens = tokens;
        SaveCount++;
        Raise();
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _tokens = null;
        ClearCount++;
        Raise();
        return Task.CompletedTask;
    }

    private void Raise()
    {
        ChangedCount++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
