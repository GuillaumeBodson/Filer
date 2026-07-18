using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Filer.Modules.BackgroundJobs.Messaging;

/// <summary>
/// Lazily-opened, shared broker connection (one per process — connections are
/// heavyweight; channels are per-component). Reconnects on demand after a broker
/// outage: callers always go through <see cref="GetOrConnectAsync"/> and never
/// cache the <see cref="IConnection"/> across calls.
/// </summary>
internal sealed class RabbitMqConnection(IOptions<BackgroundJobsOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetOrConnectAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            BackgroundJobsOptions.RabbitMqOptions mq = options.Value.RabbitMq;
            var factory = new ConnectionFactory
            {
                HostName = mq.HostName,
                Port = mq.Port,
                UserName = mq.UserName,
                Password = mq.Password,
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}
