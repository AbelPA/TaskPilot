using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Contracts;
using Api.AudioExtractions.Services;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Api.AudioExtractions.Messaging;

public sealed class AudioExtractionOutcomeConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> rabbitOptions,
    ILogger<AudioExtractionOutcomeConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Audio outcome consumer disconnected ({ExceptionType}); it will retry.",
                    exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var settings = rabbitOptions.Value;
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.UserName,
            Password = settings.Password,
        };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(true, true),
            stoppingToken);
        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 16, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };
        channel.ChannelShutdownAsync += (_, _) =>
        {
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var payload = delivery.Body.ToArray();
            object message;
            try
            {
                message = DeserializeOutcome(payload, delivery.RoutingKey);
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidDataException or NotSupportedException)
            {
                await DeadLetterAsync(channel, payload, delivery.RoutingKey, stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                logger.LogWarning("Malformed audio outcome sent to the dead-letter queue.");
                return;
            }

            try
            {
                var traceParent = GetTraceParent(delivery.BasicProperties);
                if (!string.IsNullOrWhiteSpace(traceParent))
                {
                    logger.LogInformation(
                        "Processing audio outcome with trace {TraceId}.",
                        traceParent.Split('-')[1]);
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<AudioExtractionOutcomeService>();
                var outcome = message switch
                {
                    AudioExtractionCompleted completed =>
                        await service.ApplyAsync(completed, stoppingToken),
                    AudioExtractionFailed failed =>
                        await service.ApplyAsync(failed, stoppingToken),
                    _ => throw new InvalidDataException("Unsupported audio outcome event."),
                };
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                if (outcome is null)
                {
                    logger.LogInformation("Audio outcome references no active request and was acknowledged.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                await DeadLetterAsync(channel, payload, delivery.RoutingKey, stoppingToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                logger.LogWarning("Invalid audio outcome sent to the dead-letter queue.");
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Audio outcome processing failed ({ExceptionType}); delivery will be retried.",
                    exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                await channel.BasicNackAsync(
                    delivery.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            RabbitMqTopology.NotificationQueue,
            autoAck: false,
            consumer,
            stoppingToken);
        var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        var completed = await Task.WhenAny(shutdown.Task, cancelled);
        if (completed != shutdown.Task)
        {
            await cancelled;
        }
    }

    private static object DeserializeOutcome(byte[] payload, string routingKey)
    {
        using var document = JsonDocument.Parse(payload);
        EnsureUniqueProperties(document.RootElement);
        if (routingKey == RabbitMqTopology.CompletedRoutingKey)
        {
            return JsonSerializer.Deserialize<AudioExtractionCompleted>(payload, SerializerOptions)
                   ?? throw new InvalidDataException("The completion event is empty.");
        }

        if (routingKey == RabbitMqTopology.FailedRoutingKey)
        {
            return JsonSerializer.Deserialize<AudioExtractionFailed>(payload, SerializerOptions)
                   ?? throw new InvalidDataException("The failure event is empty.");
        }

        throw new InvalidDataException("The audio outcome routing key is not supported.");
    }

    private static void EnsureUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Duplicate JSON event properties are not allowed.");
                }

                EnsureUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureUniqueProperties(item);
            }
        }
    }

    private static string? GetTraceParent(IReadOnlyBasicProperties? properties)
    {
        if (properties?.Headers is null)
        {
            return null;
        }

        if (properties.Headers.TryGetValue("traceparent", out var value) && value is byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        if (properties.Headers.TryGetValue("traceparent", out var text) && text is string textValue)
        {
            return textValue;
        }

        return null;
    }

    private static async Task DeadLetterAsync(
        IChannel channel,
        byte[] payload,
        string routingKey,
        CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                failureCode = "MALFORMED_OUTCOME",
                bodySha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                routingKey,
            },
            SerializerOptions);
        await channel.BasicPublishAsync(
            RabbitMqTopology.DeadLetterExchange,
            "audio.extraction.outcome.malformed",
            mandatory: true,
            new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
            },
            envelope,
            cancellationToken);
    }
}
