using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Filer.Modules.BackgroundJobs.Messaging;

/// <summary>
/// Readiness check for the broker, registered only when RabbitMQ dispatch is
/// active (#159 pattern: the module owns its readiness signal). Broker-down is
/// degraded-but-working — the sweeper still processes — so it reports
/// <see cref="HealthStatus.Degraded"/>, not the registration failure status:
/// /health/ready stays 200 and the state is visible in the payload/monitoring.
/// </summary>
internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IConnection conn = await connection.GetOrConnectAsync(cancellationToken);
            return conn.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ connection is open.")
                : HealthCheckResult.Degraded("RabbitMQ connection is closed; dispatch degrades to the sweeper.");
        }
        catch (Exception ex) when (ex is BrokerUnreachableException or AlreadyClosedException
            or OperationInterruptedException or TimeoutException or SocketException)
        {
            return HealthCheckResult.Degraded("RabbitMQ broker is unreachable; dispatch degrades to the sweeper.", ex);
        }
    }
}
