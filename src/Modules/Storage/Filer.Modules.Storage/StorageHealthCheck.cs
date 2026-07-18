using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Filer.Modules.Storage;

/// <summary>
/// Readiness check for the storage backend (#159, 04-non-functional.md): proves
/// the blob root is writable with a create-then-delete probe file. Mirrors
/// <see cref="LocalFileSystemStorageProvider"/> semantics — the root is created
/// on demand, so an absent directory is provisioned, not failed. A future S3/Blob
/// provider ships its own check; the module owns its readiness signal.
/// </summary>
internal sealed class StorageHealthCheck(IOptions<StorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string root = Path.GetFullPath(options.Value.RootPath);
            Directory.CreateDirectory(root);

            // Probe files never collide with blobs: keys are 64-char hex (05),
            // and the probe is deleted before the check returns.
            string probePath = Path.Combine(root, ".health-" + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllBytesAsync(probePath, [], cancellationToken);
            File.Delete(probePath);

            return HealthCheckResult.Healthy("Storage root is writable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Path detail stays out of the payload (05); the exception is logged
            // by the health-check middleware, not exposed to callers.
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "Storage root is not writable.",
                ex);
        }
    }
}
