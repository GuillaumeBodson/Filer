using System.Globalization;
using Bunit;
using Filer.ApiClient.Auth;
using Filer.Web.Auth;
using FluentAssertions;
using Xunit;
using TestContext = Xunit.TestContext;

namespace Filer.Ui.Tests.Auth;

public sealed class LocalStorageTokenStoreTests
{
    private const string StorageKey = "filer.tokens";

    private static BunitJSInterop StrictJs() => new() { Mode = JSRuntimeMode.Strict };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetAsync_returns_null_when_nothing_is_stored(string? stored)
    {
        var js = StrictJs();
        js.Setup<string?>("localStorage.getItem", StorageKey).SetResult(stored);
        var store = new LocalStorageTokenStore(js.JSRuntime);

        TokenPair? result = await store.GetAsync(TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    public async Task GetAsync_returns_null_for_corrupted_or_legacy_json_instead_of_throwing(string stored)
    {
        var js = StrictJs();
        js.Setup<string?>("localStorage.getItem", StorageKey).SetResult(stored);
        var store = new LocalStorageTokenStore(js.JSRuntime);

        TokenPair? result = await store.GetAsync(TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Save_then_get_round_trips_the_pair_as_camelCase_json()
    {
        var js = StrictJs();
        var setItem = js.SetupVoid("localStorage.setItem", _ => true);
        setItem.SetVoidResult();
        var store = new LocalStorageTokenStore(js.JSRuntime);
        var pair = new TokenPair(
            "access-1",
            DateTimeOffset.Parse("2099-01-01T00:00:00+00:00", CultureInfo.InvariantCulture),
            "refresh-1",
            DateTimeOffset.Parse("2099-02-01T00:00:00+00:00", CultureInfo.InvariantCulture));

        await store.SaveAsync(pair, TestContext.Current.CancellationToken);

        var invocation = setItem.Invocations.Should().ContainSingle().Subject;
        invocation.Arguments[0].Should().Be(StorageKey);
        // camelCase is the persisted contract; a casing change would strand existing sessions.
        string json = (string)invocation.Arguments[1]!;
        json.Should().Contain("\"accessToken\"").And.Contain("\"refreshToken\"");

        js.Setup<string?>("localStorage.getItem", StorageKey).SetResult(json);
        TokenPair? roundTripped = await store.GetAsync(TestContext.Current.CancellationToken);

        roundTripped.Should().Be(pair);
    }

    [Fact]
    public async Task ClearAsync_removes_the_storage_key()
    {
        var js = StrictJs();
        var removeItem = js.SetupVoid("localStorage.removeItem", StorageKey);
        removeItem.SetVoidResult();
        var store = new LocalStorageTokenStore(js.JSRuntime);

        await store.ClearAsync(TestContext.Current.CancellationToken);

        removeItem.Invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task Changed_is_raised_on_save_and_on_clear()
    {
        var js = StrictJs();
        js.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        js.SetupVoid("localStorage.removeItem", StorageKey).SetVoidResult();
        var store = new LocalStorageTokenStore(js.JSRuntime);
        var changed = 0;
        store.Changed += (_, _) => changed++;

        await store.SaveAsync(new TokenPair("access", null, "refresh", null), TestContext.Current.CancellationToken);
        changed.Should().Be(1);

        await store.ClearAsync(TestContext.Current.CancellationToken);
        changed.Should().Be(2);
    }
}
