using System.Data.Common;
using System.Diagnostics;
using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Messaging;
using Api.AudioExtractions.Observability;
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
    private static readonly ActivitySource ActivitySource = new("TaskPilot.AudioExtraction");

    public async Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
    {
        using var publishActivity = StartPublishActivity(message);
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
            Headers = GetTraceHeaders(message),
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

    internal static Activity? StartPublishActivity(OutboxMessageEntity message)
    {
        ActivityContext? parentContext = null;
        if (!string.IsNullOrWhiteSpace(message.TraceParent) &&
            ActivityContext.TryParse(message.TraceParent, message.TraceState, out var parsedContext))
        {
            parentContext = parsedContext;
        }

        var activity = ActivitySource.StartActivity(
            "audio_extraction.publish",
            ActivityKind.Producer,
            parentContext ?? default);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", RabbitMqTopology.Exchange);
        activity?.SetTag("messaging.operation.type", "publish");
        return activity;
    }

    internal static IDictionary<string, object?>? GetTraceHeaders(OutboxMessageEntity message)
    {
        var headers = new Dictionary<string, object?>();
        var traceParent = TraceContext.GetCurrentTraceParent() ?? message.TraceParent;
        var traceState = Activity.Current?.TraceStateString ?? message.TraceState;
        if (!string.IsNullOrWhiteSpace(traceParent) &&
            ActivityContext.TryParse(traceParent, traceState, out _))
        {
            headers["traceparent"] = traceParent;
            if (!string.IsNullOrWhiteSpace(traceState))
            {
                headers["tracestate"] = traceState;
            }
        }

        return headers.Count == 0 ? null : headers;
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
                logger.LogError(
                    "Outbox database operation failed ({ExceptionType}); pending messages are retained.",
                    exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (DbException exception)
            {
                logger.LogError(
                    "Outbox database operation failed ({ExceptionType}); pending messages are retained.",
                    exception.GetType().Name);
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
                        "Outbox delivery remains pending after {AttemptCount} attempts ({ExceptionType}); message is retained for slower retry.",
                        message.AttemptCount,
                        exception.GetType().Name);
                }
                else
                {
                    logger.LogWarning(
                        "Outbox delivery attempt {AttemptCount} failed ({ExceptionType}); message remains pending.",
                        message.AttemptCount,
                        exception.GetType().Name);
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

    private static IDictionary<string, object?>? GetTraceHeaders(OutboxMessageEntity message)
    {
        var headers = new Dictionary<string, object?>();
        var assignedTraceParent = !string.IsNullOrWhiteSpace(message.TraceParent)
            ? message.TraceParent
            : TraceContext.GetCurrentTraceParent();
        if (!string.IsNullOrWhiteSpace(assignedTraceParent))
        {
            headers["traceparent"] = assignedTraceParent;
        }

        var assignedTraceState = !string.IsNullOrWhiteSpace(message.TraceState)
            ? message.TraceState
            : Activity.Current?.TraceStateString;
        if (!string.IsNullOrWhiteSpace(assignedTraceState))
        {
            headers["tracestate"] = assignedTraceState;
        }

        return headers.Count == 0 ? null : headers;
    }
}
