namespace Filer.Modules.Storage.Contracts;

/// <summary>
/// Abstraction over binary blob storage (07-storage-and-deployment.md). Metadata
/// lives in PostgreSQL; the bytes live behind this interface, referenced by an
/// opaque, non-guessable storage key (05-security.md). Consumers treat the key as
/// a token — never a path — and depend on this interface only, never on a concrete
/// provider (10-solution-structure.md, rule 5).
/// </summary>
/// <remarks>
/// Failures at this seam are infrastructural, not business outcomes: implementations
/// throw (I/O failure, missing blob) and the calling feature service translates them
/// into its own <c>Result</c>/<c>Error</c> where that is meaningful
/// (13-code-quality-and-design.md, "Result vs exceptions").
/// </remarks>
public interface IFileStorageProvider
{
    /// <summary>
    /// Persists <paramref name="content"/> and returns the opaque storage key the
    /// blob is retrievable under. The write is atomic: a blob is either fully
    /// stored or absent, never half-written (04-non-functional.md).
    /// </summary>
    /// <param name="content">The blob bytes; read from its current position.</param>
    /// <param name="contentType">
    /// The declared MIME type. Backends that persist object metadata (S3/Blob)
    /// store it; the local provider validates and ignores it.
    /// </param>
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Opens the blob for reading. The caller owns — and must dispose — the stream.</summary>
    /// <exception cref="StorageBlobNotFoundException">No blob exists for <paramref name="storageKey"/>.</exception>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Deletes the blob. Deleting an unknown key is a no-op (idempotent).</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Whether a blob exists for <paramref name="storageKey"/>.</summary>
    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken);
}
