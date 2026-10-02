using System.Data.Common;
using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Messaging;
using Api.AudioExtractions.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Api.AudioExtractions.Persistence;

public interface IOutboxMessagePublisher
{
    Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken);
}

public sealed class RabbitMqOutboxMessagePublisher(
    IOptions<RabbitMqOptions> options) : IOutboxMessagePublisher
{
    public async Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.UserName,
            Password = settings.Password,
        };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(true, true),
            cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);

        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = message.EventId.ToString("D"),
            Type = message.EventType,
            ContentType = "application/json",
        };
        var payload = System.Text.Encoding.UTF8.GetBytes(message.PayloadJson);
        await channel.BasicPublishAsync(
            RabbitMqTopology.Exchange,
            message.EventType,
            mandatory: true,
            properties,
            payload,
            cancellationToken);
    }
}

public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IOutboxMessagePublisher messagePublisher,
    TimeProvider timeProvider,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private const int SlowRetryThreshold = 12;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishBatchAsync(stoppingToken);
                if (published == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (NpgsqlException exception)
            {
                logger.LogError(exception, "Outbox database operation failed; pending messages are retained.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (DbException exception)
            {
                logger.LogError(exception, "Outbox database operation failed; pending messages are retained.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        List<OutboxMessageEntity> messages;
        if (dbContext.Database.IsNpgsql())
        {
            messages = await dbContext.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM audio_extraction_outbox
                    WHERE "State" = 'pending' AND "NextAttemptAt" <= {now}
                    ORDER BY "NextAttemptAt", "OccurredAt"
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);
        }
        else
        {
            messages = (await dbContext.OutboxMessages.ToListAsync(cancellationToken))
                .Where(message => message.State == "pending" && message.NextAttemptAt <= now)
                .OrderBy(message => message.NextAttemptAt)
                .ThenBy(message => message.OccurredAt)
                .Take(BatchSize)
                .ToList();
        }

        foreach (var message in messages)
        {
            try
            {
                await messagePublisher.PublishAsync(message, cancellationToken);
                message.State = "published";
                message.PublishedAt = timeProvider.GetUtcNow();
            }
            catch (Exception exception) when (
                exception is BrokerUnreachableException or AlreadyClosedException or PublishException or IOException or TimeoutException ||
                exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                message.AttemptCount++;
                message.NextAttemptAt = timeProvider.GetUtcNow() + GetRetryDelay(message.AttemptCount);
                if (message.AttemptCount >= SlowRetryThreshold)
                {
                    logger.LogError(
                        exception,
                        "Outbox delivery remains pending after {AttemptCount} attempts; message is retained for slower retry.",
                        message.AttemptCount);
                }
                else
                {
                    logger.LogWarning(
                        exception,
                        "Outbox delivery attempt {AttemptCount} failed; message remains pending.",
                        message.AttemptCount);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages.Count(message => message.State == "published");
    }

    internal static TimeSpan GetRetryDelay(int attemptCount) =>
        attemptCount >= SlowRetryThreshold
            ? TimeSpan.FromMinutes(30)
            : TimeSpan.FromSeconds(Math.Min(1 << Math.Min(attemptCount, 6), 60));
}
