using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Services;
using Api.AudioExtractions.YouTube;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class CreateAudioExtractionTests
{
    [Fact]
    public async Task Valid_request_creates_request_and_pending_outbox_atomically()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(
            Request(),
            idempotencyKey: null,
            "127.0.0.1",
            CancellationToken.None);

        Assert.Matches("^[A-Za-z0-9_-]{43}$", result.RequestId);
        Assert.Equal("accepted", result.Status);
        var request = await fixture.DbContext.Requests.SingleAsync();
        var outbox = await fixture.DbContext.OutboxMessages.SingleAsync();
        Assert.Equal(request.RequestId, outbox.RequestId);
        Assert.Equal("pending", outbox.State);
        Assert.Equal(0, outbox.AttemptCount);
        Assert.Contains("\"videoId\":\"abcdefghijk\"", outbox.PayloadJson);
        Assert.DoesNotContain("youtube.com", outbox.PayloadJson);
        Assert.DoesNotContain("api-key", outbox.PayloadJson);
    }

    [Fact]
    public async Task Identical_idempotency_retry_returns_original_request()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.CreateAsync(
            Request(),
            "client-key-123456",
            "127.0.0.1",
            CancellationToken.None);
        var retry = await fixture.Service.CreateAsync(
            Request(),
            "client-key-123456",
            "127.0.0.1",
            CancellationToken.None);

        Assert.Equal(first.RequestId, retry.RequestId);
        Assert.Single(await fixture.DbContext.Requests.ToListAsync());
        Assert.Single(await fixture.DbContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Reusing_idempotency_key_with_different_interval_is_conflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.CreateAsync(
            Request(),
            "client-key-123456",
            "127.0.0.1",
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AudioExtractionValidationException>(() =>
            fixture.Service.CreateAsync(
                Request(end: "00:00:45"),
                "client-key-123456",
                "127.0.0.1",
                CancellationToken.None));

        Assert.Equal("IDEMPOTENCY_KEY_REUSED", exception.Code);
        Assert.Single(await fixture.DbContext.Requests.ToListAsync());
    }

    [Fact]
    public async Task Invalid_url_is_rejected_before_metadata_lookup_or_persistence()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<AudioExtractionValidationException>(() =>
            fixture.Service.CreateAsync(
                Request(url: "https://example.com/watch?v=abcdefghijk"),
                null,
                "127.0.0.1",
                CancellationToken.None));

        Assert.Equal("INVALID_YOUTUBE_URL", exception.Code);
        Assert.Equal(0, fixture.MetadataClient.CallCount);
        Assert.Empty(await fixture.DbContext.Requests.ToListAsync());
    }

    [Fact]
    public async Task Interval_beyond_verified_video_duration_is_rejected_without_work()
    {
        await using var fixture = await Fixture.CreateAsync(durationSeconds: 25);

        var exception = await Assert.ThrowsAsync<AudioExtractionValidationException>(() =>
            fixture.Service.CreateAsync(
                Request(),
                null,
                "127.0.0.1",
                CancellationToken.None));

        Assert.Equal("INVALID_INTERVAL", exception.Code);
        Assert.Empty(await fixture.DbContext.Requests.ToListAsync());
        Assert.Empty(await fixture.DbContext.OutboxMessages.ToListAsync());
    }

    private static CreateAudioExtractionRequest Request(
        string url = "https://www.youtube.com/watch?v=abcdefghijk",
        string start = "00:00:00",
        string end = "00:00:30") => new(url, start, end);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            AudioExtractionDbContext dbContext,
            FakeMetadataClient metadataClient)
        {
            _connection = connection;
            DbContext = dbContext;
            MetadataClient = metadataClient;
            Service = new AudioExtractionRequestService(
                DbContext,
                MetadataClient,
                Options.Create(new AudioExtractionOptions()),
                TimeProvider.System);
        }

        public AudioExtractionDbContext DbContext { get; }
        public FakeMetadataClient MetadataClient { get; }
        public AudioExtractionRequestService Service { get; }

        public static async Task<Fixture> CreateAsync(int durationSeconds = 3_600)
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
            return new Fixture(connection, dbContext, new FakeMetadataClient(durationSeconds));
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeMetadataClient(int durationSeconds) : IYouTubeVideoMetadataClient
    {
        public int CallCount { get; private set; }

        public Task<YouTubeVideoMetadata> GetVideoMetadataAsync(
            string videoId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new YouTubeVideoMetadata(durationSeconds));
        }
    }
}
