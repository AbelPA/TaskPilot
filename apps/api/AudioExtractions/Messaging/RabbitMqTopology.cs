using RabbitMQ.Client;

namespace Api.AudioExtractions.Messaging;

public static class RabbitMqTopology
{
    public const string Exchange = "media.events";
    public const string DeadLetterExchange = "media.events.dlx";
    public const string WorkerQueue = "audio.extraction.worker";
    public const string NotificationQueue = "audio.extraction.notifications";
    public const string DeadLetterQueue = "audio.extraction.dead-letter";
    public const string RequestedRoutingKey = "audio.extraction.requested";
    public const string CompletedRoutingKey = "audio.extraction.completed";
    public const string FailedRoutingKey = "audio.extraction.failed";

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            DeadLetterExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            WorkerQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            WorkerQueue,
            Exchange,
            RequestedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            NotificationQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            NotificationQueue,
            Exchange,
            CompletedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            NotificationQueue,
            Exchange,
            FailedRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            DeadLetterQueue,
            DeadLetterExchange,
            "#",
            arguments: null,
            cancellationToken: cancellationToken);

        foreach (var (name, ttl) in new[]
                 {
                     ("audio.extraction.worker.retry.5s", 5_000),
                     ("audio.extraction.worker.retry.30s", 30_000),
                     ("audio.extraction.worker.retry.180s", 180_000),
                 })
        {
            var arguments = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = ttl,
                ["x-dead-letter-exchange"] = Exchange,
                ["x-dead-letter-routing-key"] = RequestedRoutingKey,
            };
            await channel.QueueDeclareAsync(
                name,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: arguments,
                cancellationToken: cancellationToken);
            await channel.QueueBindAsync(
                name,
                Exchange,
                $"{RequestedRoutingKey}.retry.{ttl}",
                arguments: null,
                cancellationToken: cancellationToken);
        }
    }
}
