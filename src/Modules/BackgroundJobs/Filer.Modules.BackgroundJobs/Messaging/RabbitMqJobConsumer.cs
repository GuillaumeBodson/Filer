using Filer.Modules.BackgroundJobs.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Filer.Modules.BackgroundJobs.Messaging;

/// <summary>
/// Consumes the wake-up queue and runs the same claim-and-process core as the
/// polling worker (ADR-008): the message carries no job payload, so the consumer
/// simply re-runs <see cref="AnalysisJobProcessor"/> — single-claim stays
/// database-enforced and a duplicate or redelivered message is a harmless no-op
/// (the claim returns null). Connection loss triggers reconnect-with-delay; while
/// the broker is down the sweeper keeps processing, so no work is ever stranded.
/// </summary>
internal sealed class RabbitMqJobConsumer(
    RabbitMqConnection connection,
    AnalysisJobProcessor processor,
    IOptions<BackgroundJobsOptions> options,
    ILogger<RabbitMqJobConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.ConsumerDisabled();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilChannelClosesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Broker unreachable or channel-level failure: back off and retry.
                // The sweeper keeps draining the table in the meantime (ADR-008).
                logger.ConsumerConnectFailed(ex, ReconnectDelay);
            }

            try
            {
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.ConsumerStopped();
    }

    private async Task ConsumeUntilChannelClosesAsync(CancellationToken stoppingToken)
    {
        string queueName = options.Value.RabbitMq.QueueName;

        IConnection conn = await connection.GetOrConnectAsync(stoppingToken);
        await using IChannel channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);

        // Idempotent declare, mirroring the publisher, so either side can start first.
        await channel.QueueDeclareAsync(
            queueName, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: stoppingToken);

        // One unacked message at a time: processing is sequential by design in V1
        // (mirrors the single polling worker; scale-out adds consumers, not prefetch).
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var channelClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            channelClosed.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                await processor.ProcessNextAsync(stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown mid-message: leave it unacked so the broker redelivers;
                // a redelivery after the sweeper got there is a no-op claim.
            }
            catch (Exception ex)
            {
                // Bookkeeping failed (e.g. DB blip). Ack anyway: the row still holds
                // the job state and the sweeper recovers it — redelivering the bare
                // wake-up adds nothing the sweep does not (ADR-008).
                logger.ConsumerDispatchFailed(ex);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, stoppingToken);
        logger.ConsumerStarted(queueName);

        // BasicConsumeAsync returns immediately; hold the channel open until
        // shutdown is requested or the broker closes it (then reconnect).
        await using CancellationTokenRegistration registration =
            stoppingToken.Register(() => channelClosed.TrySetResult());
        await channelClosed.Task;
    }
}

/// <summary>
/// Log messages for <see cref="RabbitMqJobConsumer"/>, co-located per the house
/// convention (13-code-quality-and-design.md).
/// </summary>
internal static partial class RabbitMqJobConsumerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ job consumer is disabled by configuration.")]
    public static partial void ConsumerDisabled(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ job consumer started on queue {QueueName}.")]
    public static partial void ConsumerStarted(this ILogger logger, string queueName);

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ job consumer stopped.")]
    public static partial void ConsumerStopped(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "RabbitMQ consumer could not connect or lost its channel; retrying in {RetryDelay}. The sweeper keeps processing meanwhile.")]
    public static partial void ConsumerConnectFailed(this ILogger logger, Exception exception, TimeSpan retryDelay);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Dispatching a consumed wake-up failed; the message is acked and the sweeper will recover the job.")]
    public static partial void ConsumerDispatchFailed(this ILogger logger, Exception exception);
}
