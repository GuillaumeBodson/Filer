namespace Filer.Modules.Storage.Contracts;

/// <summary>
/// Thrown by <see cref="IFileStorageProvider.OpenReadAsync"/> when no blob exists
/// for the requested key. Metadata referencing a missing blob is an integrity
/// violation, not an expected business outcome — hence an exception rather than a
/// <c>Result</c> (13-code-quality-and-design.md); the host's global exception
/// handler turns it into a problem-details 500 without leaking internals.
/// </summary>
public sealed class StorageBlobNotFoundException : InvalidOperationException
{
    public StorageBlobNotFoundException()
        : base("No blob exists for the requested storage key.")
    {
    }

    public StorageBlobNotFoundException(string message)
        : base(message)
    {
    }

    public StorageBlobNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a specific storage key.</summary>
    public static StorageBlobNotFoundException ForKey(string storageKey) =>
        new($"No blob exists for storage key '{storageKey}'.");
}
