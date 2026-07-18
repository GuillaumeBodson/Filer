using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Filer.Modules.BackgroundJobs.Messaging;

/// <summary>
/// Publishes the "job {id} ready" wake-up to the durable queue (ADR-008), with
/// publisher confirmations: the publish await completes only once the broker has
/// accepted the message, so a silently-black-holed publish cannot be mistaken
/// for a delivered one. Failures still only cost latency, never work — the
/// decorator swallows them and the sweeper recovers the committed row.
/// </summary>
internal sealed class RabbitMqJobPublisher(
    RabbitMqConnection connection,
    IOptions<BackgroundJobsOptions> options) : IAnalysisJobDispatcher, IAsyncDisposable
{
    // Channels are not safe for concurrent use; V1 publish volume does not
    // justify a channel pool over one gated channel.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishJobReadyAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            IChannel channel = await GetOrCreateChannelAsync(cancellationToken);

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "text/plain",
            };

            // Additive trace propagation (ADR-013): the row stays the authoritative
            // carrier; the header exists for broker-native tooling from day one.
            string? traceparent = Activity.Current?.Id;
            if (traceparent is not null)
            {
                properties.Headers = new Dictionary<string, object?> { ["traceparent"] = traceparent };
            }

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: options.Value.RabbitMq.QueueName,
                mandatory: false,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(jobId.ToString("D")),
                cancellationToken: cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> GetOrCreateChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        IConnection conn = await connection.GetOrConnectAsync(cancellationToken);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        IChannel channel = await conn.CreateChannelAsync(channelOptions, cancellationToken);

        // Idempotent declare so publisher and consumer agree on the queue no
        // matter which side starts first.
        await channel.QueueDeclareAsync(
            options.Value.RabbitMq.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        _channel = channel;
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }
}
