using System.Text.Json;
using Filer.ApiClient.Auth;
using Microsoft.JSInterop;

namespace Filer.Web.Auth;

/// <summary>
/// Browser-localStorage <see cref="ITokenStore"/> for the WASM host. Token values are
/// only ever passed to the JS storage API and serialized to localStorage - never logged
/// (05-security.md). The future MAUI shell (RM-02) supplies its own secure-storage
/// implementation of this same interface.
/// </summary>
internal sealed class LocalStorageTokenStore(IJSRuntime jsRuntime) : ITokenStore
{
    private const string StorageKey = "filer.tokens";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IJSRuntime _jsRuntime = jsRuntime;

    public event EventHandler? Changed;

    public async Task<TokenPair?> GetAsync(CancellationToken cancellationToken = default)
    {
        string? json = await _jsRuntime
            .InvokeAsync<string?>("localStorage.getItem", cancellationToken, StorageKey)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TokenPair>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(TokenPair tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        string json = JsonSerializer.Serialize(tokens, SerializerOptions);
        await _jsRuntime
            .InvokeVoidAsync("localStorage.setItem", cancellationToken, StorageKey, json)
            .ConfigureAwait(false);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _jsRuntime
            .InvokeVoidAsync("localStorage.removeItem", cancellationToken, StorageKey)
            .ConfigureAwait(false);

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
