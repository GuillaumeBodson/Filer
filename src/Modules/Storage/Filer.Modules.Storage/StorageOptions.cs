using System.ComponentModel.DataAnnotations;

namespace Filer.Modules.Storage;

/// <summary>
/// Storage configuration bound from the <c>Storage</c> section. The backend and its
/// root path are configuration-driven (07-storage-and-deployment.md): deployments
/// switch providers without code changes, and no concrete provider leaks into
/// domain code.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Name of the V1 local-filesystem provider (07).</summary>
    public const string LocalProviderName = "Local";

    /// <summary>Selects the <c>IFileStorageProvider</c> implementation. V1 ships "Local".</summary>
    [Required]
    public string Provider { get; init; } = LocalProviderName;

    /// <summary>
    /// Root directory for the local provider's blobs — a mounted Docker volume in
    /// deployment (07), never a web-exposed path (05-security.md).
    /// </summary>
    [Required]
    public string RootPath { get; init; } = string.Empty;
}
