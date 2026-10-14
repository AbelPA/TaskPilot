using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
