using Filer.Modules.Storage.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Filer.Modules.Storage;

/// <summary>
/// Registration entry point for the Storage module. The host invokes
/// <see cref="AddStorageModule"/> only; it never reaches into module internals
/// (10-solution-structure.md). The module maps no endpoints: blobs are reachable
/// solely through the authenticated Documents slices (05-security.md).
/// </summary>
public static class StorageModule
{
    public static IServiceCollection AddStorageModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Eager read for provider selection, mirroring AddAuthModule: registration
        // shape is decided while the builder is still composing.
        StorageOptions options = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
            ?? throw new InvalidOperationException("The 'Storage' configuration section is missing.");

        // Provider selected by configuration (07): the host decides which backend
        // runs; everything else sees only IFileStorageProvider.
        switch (options.Provider)
        {
            case StorageOptions.LocalProviderName:
                services.AddSingleton<IFileStorageProvider, LocalFileSystemStorageProvider>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown storage provider '{options.Provider}'. Supported providers: '{StorageOptions.LocalProviderName}'.");
        }

        // The module owns its readiness signal (#159): /health/ready reflects
        // whether the configured blob root is writable. AddHealthChecks is
        // additive, so each module contributes checks independently.
        services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);

        return services;
    }
}
