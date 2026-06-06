using System.Text;
using Filer.Modules.Storage.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Filer.Modules.Storage.Tests;

/// <summary>
/// Unit tests for <see cref="LocalFileSystemStorageProvider"/> (#31): the
/// save/read/delete round-trip plus the behaviours the contract promises —
/// opaque non-guessable keys, sharded layout, atomic publication, idempotent
/// delete, and malformed keys never reaching the filesystem (05/07/12).
/// Each test gets a throwaway root directory; no shared state.
/// </summary>
public sealed class LocalFileSystemStorageProviderTests : IDisposable
{
    private const string AnyContentType = "application/pdf";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "filer-storage-tests-" + Guid.NewGuid().ToString("N"));

    private readonly LocalFileSystemStorageProvider _provider;

    public LocalFileSystemStorageProviderTests()
    {
        _provider = new LocalFileSystemStorageProvider(
            Options.Create(new StorageOptions { RootPath = _root }),
            NullLogger<LocalFileSystemStorageProvider>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_returns_an_opaque_64_char_lowercase_hex_key()
    {
        string key = await _provider.SaveAsync(Content("hello"), AnyContentType, TestContext.Current.CancellationToken);

        key.Should().HaveLength(64);
        key.Should().MatchRegex("^[0-9a-f]{64}$", "keys are opaque, non-guessable random hex (05-security.md)");
    }

    [Fact]
    public async Task SaveAsync_generates_a_distinct_key_per_save()
    {
        string first = await _provider.SaveAsync(Content("same bytes"), AnyContentType, TestContext.Current.CancellationToken);
        string second = await _provider.SaveAsync(Content("same bytes"), AnyContentType, TestContext.Current.CancellationToken);

        second.Should().NotBe(first, "every save yields its own blob; dedupe is the Documents module's concern (#33)");
    }

    [Fact]
    public async Task Save_then_OpenRead_round_trips_the_content()
    {
        string key = await _provider.SaveAsync(Content("round-trip me"), AnyContentType, TestContext.Current.CancellationToken);

        await using Stream stream = await _provider.OpenReadAsync(key, TestContext.Current.CancellationToken);
        using StreamReader reader = new(stream, Encoding.UTF8);
        string roundTripped = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        roundTripped.Should().Be("round-trip me");
    }

    [Fact]
    public async Task SaveAsync_shards_the_blob_path_by_the_key_prefix()
    {
        string key = await _provider.SaveAsync(Content("sharded"), AnyContentType, TestContext.Current.CancellationToken);

        string expectedPath = Path.Combine(_root, key[..2], key.Substring(2, 2), key);
        File.Exists(expectedPath).Should().BeTrue("the layout shards by the first key bytes to avoid huge flat directories (07)");
    }

    [Fact]
    public async Task ExistsAsync_is_true_for_a_saved_blob_and_false_for_an_unknown_key()
    {
        string key = await _provider.SaveAsync(Content("present"), AnyContentType, TestContext.Current.CancellationToken);
        string unknownKey = new('0', 64);

        (await _provider.ExistsAsync(key, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await _provider.ExistsAsync(unknownKey, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_blob()
    {
        string key = await _provider.SaveAsync(Content("doomed"), AnyContentType, TestContext.Current.CancellationToken);

        await _provider.DeleteAsync(key, TestContext.Current.CancellationToken);

        (await _provider.ExistsAsync(key, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_is_a_noop_for_an_unknown_key()
    {
        string unknownKey = new('a', 64);

        Func<Task> act = () => _provider.DeleteAsync(unknownKey, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync("delete is idempotent by contract");
    }

    [Fact]
    public async Task OpenReadAsync_throws_StorageBlobNotFound_for_an_unknown_key()
    {
        string unknownKey = new('b', 64);

        Func<Task> act = () => _provider.OpenReadAsync(unknownKey, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<StorageBlobNotFoundException>(
            "metadata referencing a missing blob is an integrity violation, not a business outcome (13)");
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData(@"..\..\secrets.json")]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")] // uppercase
    [InlineData("zzzz456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")] // non-hex
    public async Task Malformed_keys_read_as_not_found_and_never_reach_the_filesystem(string malformedKey)
    {
        (await _provider.ExistsAsync(malformedKey, TestContext.Current.CancellationToken))
            .Should().BeFalse("a structurally invalid key must never resolve to a path (05: traversal)");

        Func<Task> read = () => _provider.OpenReadAsync(malformedKey, TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<StorageBlobNotFoundException>();

        Func<Task> delete = () => _provider.DeleteAsync(malformedKey, TestContext.Current.CancellationToken);
        await delete.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveAsync_rejects_a_null_stream_and_a_blank_content_type()
    {
        Func<Task> nullContent = () => _provider.SaveAsync(null!, AnyContentType, TestContext.Current.CancellationToken);
        await nullContent.Should().ThrowAsync<ArgumentNullException>();

        Func<Task> blankContentType = () => _provider.SaveAsync(Content("x"), " ", TestContext.Current.CancellationToken);
        await blankContentType.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SaveAsync_publishes_no_blob_when_the_copy_fails()
    {
        Func<Task> act = () => _provider.SaveAsync(new FailingStream(), AnyContentType, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>();
        AllFilesUnderRoot().Should().BeEmpty("a blob is either fully present or absent — no partial temp files (04/07)");
    }

    [Fact]
    public async Task SaveAsync_honours_cancellation_and_leaves_nothing_behind()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => _provider.SaveAsync(Content("never stored"), AnyContentType, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        AllFilesUnderRoot().Should().BeEmpty();
    }

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    private string[] AllFilesUnderRoot() =>
        Directory.Exists(_root)
            ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            : [];

    /// <summary>A stream whose first read fails, simulating an interrupted upload body.</summary>
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Simulated read failure.");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("Simulated read failure.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated read failure.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
