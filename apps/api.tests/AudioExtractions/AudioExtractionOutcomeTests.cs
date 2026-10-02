using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Contracts;
using Api.AudioExtractions.Notifications;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Persistence.Entities;
using Api.AudioExtractions.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class AudioExtractionOutcomeTests
{
    private const string RequestId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Completion_is_committed_before_group_notification()
    {
        await using var fixture = await Fixture.CreateAsync();
        var publisher = new RecordingPublisher(async notification =>
        {
            fixture.DbContext.ChangeTracker.Clear();
            Assert.Equal("completed", (await fixture.DbContext.Requests.SingleAsync()).Status);
            Assert.Single(await fixture.DbContext.NotificationOutcomes.AsNoTracking().ToListAsync());
            Assert.Equal(30, notification.Result!.DurationSeconds);
        });
        var service = fixture.CreateService(publisher);

        var response = await service.ApplyAsync(Completion(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("completed", response!.Status);
        Assert.Equal($"/api/audio-extractions/{RequestId}/audio", response.Result!.AudioPath);
        Assert.Equal("audio/mpeg", response.Result.ContentType);
        Assert.DoesNotContain("objectKey", System.Text.Json.JsonSerializer.Serialize(response));
        Assert.Single(await fixture.DbContext.ProcessedEvents.ToListAsync());
        Assert.Single(publisher.Notifications);
    }

    [Fact]
    public async Task Failure_persists_a_safe_message_and_the_stable_failure_code()
    {
        await using var fixture = await Fixture.CreateAsync();
        var publisher = new RecordingPublisher();
        var service = fixture.CreateService(publisher);
        var failed = new AudioExtractionFailed(
            Guid.NewGuid(),
            "audio.extraction.failed",
            1,
            RequestId,
            DateTimeOffset.UtcNow,
            new AudioExtractionFailedData(
                "DOWNLOAD_FAILED",
                "raw exception with a private source URL",
                false));

        var response = await service.ApplyAsync(failed, CancellationToken.None);

        fixture.DbContext.ChangeTracker.Clear();
        var request = await fixture.DbContext.Requests.SingleAsync();
        Assert.Equal("failed", request.Status);
        Assert.Equal("DOWNLOAD_FAILED", request.FailureCode);
        Assert.DoesNotContain("private source URL", request.FailureMessage);
        Assert.Equal("Não foi possível obter o áudio do vídeo.", response!.Message);
        Assert.Null(response.Result);
    }

    [Fact]
    public async Task Unknown_request_does_not_create_an_outcome_or_notification()
    {
        await using var fixture = await Fixture.CreateAsync();
        var publisher = new RecordingPublisher();
        var service = fixture.CreateService(publisher);

        var response = await service.ApplyAsync(
            Completion(Guid.NewGuid()) with
            {
                RequestId = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            },
            CancellationToken.None);

        Assert.Null(response);
        Assert.Empty(await fixture.DbContext.NotificationOutcomes.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.DbContext.ProcessedEvents.AsNoTracking().ToListAsync());
        Assert.Empty(publisher.Notifications);
    }

    [Fact]
    public async Task Duplicate_and_stale_terminal_events_do_not_create_multiple_outcomes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var publisher = new RecordingPublisher();
        var service = fixture.CreateService(publisher);
        var completion = Completion(Guid.NewGuid());

        await service.ApplyAsync(completion, CancellationToken.None);
        await service.ApplyAsync(completion, CancellationToken.None);
        await service.ApplyAsync(
            Failure(Guid.NewGuid(), "EXTRACTION_FAILED"),
            CancellationToken.None);

        fixture.DbContext.ChangeTracker.Clear();
        var request = await fixture.DbContext.Requests.SingleAsync();
        Assert.Equal("completed", request.Status);
        Assert.Single(await fixture.DbContext.NotificationOutcomes.AsNoTracking().ToListAsync());
        Assert.Equal(2, (await fixture.DbContext.ProcessedEvents.AsNoTracking().ToListAsync()).Count);
        Assert.Equal(2, publisher.Notifications.Count);
        Assert.All(publisher.Notifications, notification => Assert.Equal("completed", notification.Status));
    }

    [Fact]
    public async Task Status_recovery_hides_malformed_unknown_and_expired_capabilities()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService(new RecordingPublisher());

        var accepted = await service.GetStatusAsync(RequestId, CancellationToken.None);
        var malformed = await service.GetStatusAsync("not-a-capability", CancellationToken.None);
        var unknown = await service.GetStatusAsync(
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            CancellationToken.None);

        Assert.Equal("accepted", accepted!.Status);
        Assert.Null(accepted.Result);
        Assert.Null(malformed);
        Assert.Null(unknown);

        var request = await fixture.DbContext.Requests.SingleAsync();
        request.Status = "failed";
        request.FailureCode = "DOWNLOAD_FAILED";
        request.FailureMessage = "safe";
        request.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Null(await service.GetStatusAsync(RequestId, CancellationToken.None));
    }

    private static AudioExtractionCompleted Completion(Guid eventId) => new(
        eventId,
        "audio.extraction.completed",
        1,
        RequestId,
        DateTimeOffset.UtcNow,
        new AudioExtractionCompletedData(
            $"audio-extractions/{RequestId}/audio.mp3",
            "audio/mpeg",
            30,
            50,
            new string('a', 64)));

    private static AudioExtractionFailed Failure(Guid eventId, string code) => new(
        eventId,
        "audio.extraction.failed",
        1,
        RequestId,
        DateTimeOffset.UtcNow,
        new AudioExtractionFailedData(code, "safe message", false));

    private sealed class RecordingPublisher(
        Func<AudioExtractionStatusResponse, Task>? callback = null)
        : IAudioExtractionNotificationPublisher
    {
        public List<AudioExtractionStatusResponse> Notifications { get; } = [];

        public async Task PublishAsync(
            AudioExtractionStatusResponse notification,
            CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            if (callback is not null)
            {
                await callback(notification);
            }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, AudioExtractionDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
        }

        public AudioExtractionDbContext DbContext { get; }

        public static async Task<Fixture> CreateAsync()
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
            SQLitePCL.raw.FreezeProvider();
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AudioExtractionDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AudioExtractionDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Requests.Add(new AudioExtractionRequestEntity
            {
                RequestId = RequestId,
                VideoId = "abcdefghijk",
                StartSeconds = 0,
                EndSeconds = 30,
                Status = "accepted",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                SourceDurationSeconds = 60,
            });
            await dbContext.SaveChangesAsync();
            return new Fixture(connection, dbContext);
        }

        public AudioExtractionOutcomeService CreateService(RecordingPublisher publisher) =>
            new(
                DbContext,
                publisher,
                Options.Create(new AudioExtractionOptions()),
                TimeProvider.System);

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
