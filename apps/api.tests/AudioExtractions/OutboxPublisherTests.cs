using System.Diagnostics;
using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class OutboxPublisherTests
{
    [Fact]
    public async Task Confirmed_publish_marks_outbox_as_published_with_stable_event_id()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventId = await fixture.SeedMessageAsync();
        var messagePublisher = new FakePublisher();
        var publisher = fixture.CreatePublisher(messagePublisher);

        var published = await publisher.PublishBatchAsync(CancellationToken.None);

        Assert.Equal(1, published);
        Assert.Equal(eventId, Assert.Single(messagePublisher.PublishedEventIds));
        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.OutboxMessages.SingleAsync();
        Assert.Equal("published", message.State);
        Assert.Equal(eventId, message.EventId);
        Assert.NotNull(message.PublishedAt);
    }

    [Fact]
    public async Task Failed_publish_retains_message_and_schedules_retry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventId = await fixture.SeedMessageAsync();
        var messagePublisher = new FakePublisher { Failure = new TimeoutException("broker timeout") };
        var publisher = fixture.CreatePublisher(messagePublisher);

        var published = await publisher.PublishBatchAsync(CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Equal(eventId, Assert.Single(messagePublisher.PublishedEventIds));
        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.OutboxMessages.SingleAsync();
        Assert.Equal("pending", message.State);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(eventId, message.EventId);
        Assert.Null(message.PublishedAt);
        Assert.True(message.NextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Reconnected_publisher_retries_the_same_event_and_marks_it_published()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventId = await fixture.SeedMessageAsync();
        var messagePublisher = new FakePublisher { Failure = new TimeoutException("broker timeout") };
        var publisher = fixture.CreatePublisher(messagePublisher);
        await publisher.PublishBatchAsync(CancellationToken.None);

        messagePublisher.Failure = null;
        await using (var retryContext = fixture.CreateDbContext())
        {
            var retryMessage = await retryContext.OutboxMessages.SingleAsync();
            retryMessage.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await retryContext.SaveChangesAsync();
        }

        await publisher.PublishBatchAsync(CancellationToken.None);

        Assert.Equal([eventId, eventId], messagePublisher.PublishedEventIds);
        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.OutboxMessages.SingleAsync();
        Assert.Equal("published", message.State);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.PublishedAt);
    }

    [Fact]
    public void RabbitMq_publish_span_continues_saved_w3c_context_and_propagates_its_child()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "TaskPilot.AudioExtraction",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var request = new Activity("http request").SetIdFormat(ActivityIdFormat.W3C).Start();
        Assert.NotNull(request);
        var message = new OutboxMessageEntity
        {
            EventId = Guid.NewGuid(),
            RequestId = new string('a', 43),
            EventType = "audio.extraction.requested",
            SchemaVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            PayloadJson = "{}",
            State = "pending",
            NextAttemptAt = DateTimeOffset.UtcNow,
            TraceParent = request!.Id,
            TraceState = "vendor=value",
        };

        using var publish = RabbitMqOutboxMessagePublisher.StartPublishActivity(message);

        Assert.NotNull(publish);
        Assert.Equal(request.TraceId, publish.TraceId);
        Assert.Equal(request.SpanId, publish.ParentSpanId);
        var headers = RabbitMqOutboxMessagePublisher.GetTraceHeaders(message);
        Assert.Equal(publish.Id, Assert.IsType<string>(headers!["traceparent"]));
        Assert.Equal("vendor=value", Assert.IsType<string>(headers["tracestate"]));
    }

    [Fact]
    public void Api_logging_includes_activity_trace_and_span_ids()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AudioExtraction"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["AudioExtraction:YouTubeApiKey"] = "test-key",
                ["AudioExtraction:StorageEndpoint"] = "http://localhost:9000",
                ["AudioExtraction:StorageAccessKey"] = "test-access",
                ["AudioExtraction:StorageSecretKey"] = "test-secret",
                ["AudioExtraction:StorageBucket"] = "test-bucket",
            })
            .Build();
        services.AddAudioExtractions(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LoggerFactoryOptions>>().Value;

        Assert.True(options.ActivityTrackingOptions.HasFlag(ActivityTrackingOptions.TraceId));
        Assert.True(options.ActivityTrackingOptions.HasFlag(ActivityTrackingOptions.SpanId));
    }

    [Fact]
    public void Invalid_saved_w3c_context_is_not_forwarded()
    {
        var message = new OutboxMessageEntity
        {
            EventId = Guid.NewGuid(),
            RequestId = new string('a', 43),
            EventType = "audio.extraction.requested",
            SchemaVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            PayloadJson = "{}",
            State = "pending",
            NextAttemptAt = DateTimeOffset.UtcNow,
            TraceParent = "invalid",
        };

        Assert.Null(RabbitMqOutboxMessagePublisher.GetTraceHeaders(message));
    }

    [Fact]
    public void Retry_delay_becomes_slower_after_the_alert_threshold()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), OutboxPublisher.GetRetryDelay(6));
        Assert.Equal(TimeSpan.FromMinutes(30), OutboxPublisher.GetRetryDelay(12));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;
        private readonly DbContextOptions<AudioExtractionDbContext> _dbOptions;

        private Fixture(
            SqliteConnection connection,
            ServiceProvider serviceProvider,
            DbContextOptions<AudioExtractionDbContext> dbOptions)
        {
            _connection = connection;
            _serviceProvider = serviceProvider;
            _dbOptions = dbOptions;
        }

        public static async Task<Fixture> CreateAsync()
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
            SQLitePCL.raw.FreezeProvider();
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var dbOptions = new DbContextOptionsBuilder<AudioExtractionDbContext>()
                .UseSqlite(connection)
                .Options;
            var services = new ServiceCollection();
            services.AddScoped(_ => new AudioExtractionDbContext(dbOptions));
            var provider = services.BuildServiceProvider();
            await using var context = new AudioExtractionDbContext(dbOptions);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, provider, dbOptions);
        }

        public AudioExtractionDbContext CreateDbContext() => new(_dbOptions);

        public OutboxPublisher CreatePublisher(IOutboxMessagePublisher messagePublisher) =>
            new(
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                messagePublisher,
                TimeProvider.System,
                NullLogger<OutboxPublisher>.Instance);

        public async Task<Guid> SeedMessageAsync()
        {
            await using var context = CreateDbContext();
            var requestId = new string('a', 43);
            var request = new AudioExtractionRequestEntity
            {
                RequestId = requestId,
                VideoId = "abcdefghijk",
                StartSeconds = 0,
                EndSeconds = 30,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                SourceDurationSeconds = 60,
            };
            var eventId = Guid.NewGuid();
            context.Requests.Add(request);
            context.OutboxMessages.Add(new OutboxMessageEntity
            {
                EventId = eventId,
                RequestId = requestId,
                EventType = "audio.extraction.requested",
                SchemaVersion = 1,
                OccurredAt = DateTimeOffset.UtcNow,
                PayloadJson = "{}",
                State = "pending",
                NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Request = request,
            });
            await context.SaveChangesAsync();
            return eventId;
        }

        public async ValueTask DisposeAsync()
        {
            await _serviceProvider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakePublisher : IOutboxMessagePublisher
    {
        public List<Guid> PublishedEventIds { get; } = [];
        public Exception? Failure { get; set; }

        public Task PublishAsync(OutboxMessageEntity message, CancellationToken cancellationToken)
        {
            PublishedEventIds.Add(message.EventId);
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }
    }
}
