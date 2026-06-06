using System.Security.Cryptography;
using Filer.Modules.Storage.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Filer.Modules.Storage;

/// <summary>
/// V1 <see cref="IFileStorageProvider"/>: blobs on a local filesystem path mounted
/// as a Docker volume (07-storage-and-deployment.md). Keys are opaque, non-guessable
/// random hex (05-security.md); the layout shards by the first key bytes to avoid
/// huge flat directories. The directory is never web-exposed — all reads flow
/// through the authenticated download endpoint.
/// </summary>
/// <remarks>
/// Keys are validated strictly before touching the filesystem, so a malformed or
/// hostile key (path traversal) can never resolve to a path: it simply behaves as
/// "not found". A crash mid-save can leave an orphaned <c>*.tmp</c> file; it is
/// never visible through the contract and is cleaned up by the retention sweep
/// planned with soft-delete purging (04/07).
/// </remarks>
public sealed class LocalFileSystemStorageProvider(
    IOptions<StorageOptions> options,
    ILogger<LocalFileSystemStorageProvider> logger) : IFileStorageProvider
{
    // 32 random bytes as lowercase hex — opaque and non-guessable (05).
    private const int KeyHexLength = 64;
    private const int ShardLength = 2;
    private const int CopyBufferSize = 81920;

    private readonly string _rootPath = Path.GetFullPath(options.Value.RootPath);
    private readonly ILogger<LocalFileSystemStorageProvider> _logger = logger;

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The local provider stores raw bytes only; contentType belongs to backends
        // that persist object metadata (S3/Blob). Validate it anyway so misuse fails
        // identically on every provider.
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        string storageKey = RandomNumberGenerator.GetHexString(KeyHexLength, lowercase: true);
        string blobPath = BlobPath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);

        // Write to a sibling temp file, then publish with an atomic move: a blob is
        // either fully present or absent, never half-written (04).
        string tempPath = blobPath + ".tmp";
        try
        {
            FileStream destination = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
            await using (destination)
            {
                await content.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, blobPath);
        }
        catch
        {
            // Best-effort cleanup of the partial temp file; the original exception
            // propagates untouched (13: never catch to hide).
            TryDeleteFile(tempPath);
            throw;
        }

        _logger.BlobSaved(storageKey);
        return storageKey;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveBlobPath(storageKey, out string blobPath) || !File.Exists(blobPath))
        {
            throw StorageBlobNotFoundException.ForKey(storageKey);
        }

        Stream stream = new FileStream(blobPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryResolveBlobPath(storageKey, out string blobPath) && File.Exists(blobPath))
        {
            File.Delete(blobPath);
            _logger.BlobDeleted(storageKey);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool exists = TryResolveBlobPath(storageKey, out string blobPath) && File.Exists(blobPath);
        return Task.FromResult(exists);
    }

    /// <summary>
    /// Resolves a key to its sharded blob path — only when the key is structurally
    /// valid. Anything else (wrong length, non-hex, traversal attempts) never
    /// reaches the filesystem and reads as "not found".
    /// </summary>
    private bool TryResolveBlobPath(string storageKey, out string blobPath)
    {
        ArgumentNullException.ThrowIfNull(storageKey);

        if (storageKey.Length != KeyHexLength || !storageKey.All(char.IsAsciiHexDigitLower))
        {
            blobPath = string.Empty;
            return false;
        }

        blobPath = BlobPath(storageKey);
        return true;
    }

    /// <summary>Sharded layout: <c>{root}/ab/cd/abcd…</c> from the key's first bytes (07).</summary>
    private string BlobPath(string storageKey) =>
        Path.Combine(_rootPath, storageKey[..ShardLength], storageKey.Substring(ShardLength, ShardLength), storageKey);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Cleanup is best-effort; the original failure is the one that matters.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: never mask the original exception with a cleanup failure.
        }
    }
}

/// <summary>
/// <c>[LoggerMessage]</c> extensions for <see cref="LocalFileSystemStorageProvider"/> —
/// structured, allocation-free logging per the house convention (13, CA1848).
/// Storage keys are opaque references, not secrets; logging them is safe (05).
/// </summary>
internal static partial class LocalFileSystemStorageProviderLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Stored blob {StorageKey}")]
    public static partial void BlobSaved(this ILogger logger, string storageKey);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Deleted blob {StorageKey}")]
    public static partial void BlobDeleted(this ILogger logger, string storageKey);
}
