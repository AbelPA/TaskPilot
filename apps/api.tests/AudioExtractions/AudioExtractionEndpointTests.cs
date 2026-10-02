using System.Net;
using System.Net.Http.Json;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.YouTube;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class AudioExtractionEndpointTests
{
    [Fact]
    public async Task Valid_request_returns_202_only_after_request_and_outbox_are_durable()
    {
        await using var factory = new AudioApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync(
            "/api/audio-extractions",
            ValidRequest());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AcceptedResponse>();
        Assert.NotNull(body);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", body.RequestId);
        Assert.Equal("accepted", body.Status);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.Single(await dbContext.Requests.ToListAsync());
        var outbox = await dbContext.OutboxMessages.SingleAsync();
        Assert.Equal(body.RequestId, outbox.RequestId);
        Assert.Equal("pending", outbox.State);
    }

    [Fact]
    public async Task Unsupported_url_returns_problem_details_without_enqueuing()
    {
        await using var factory = new AudioApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync(
            "/api/audio-extractions",
            ValidRequest(url: "https://example.com/watch?v=abcdefghijk"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.Empty(await dbContext.Requests.ToListAsync());
        Assert.Empty(await dbContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Same_idempotency_key_reuses_request_and_conflicting_body_returns_409()
    {
        await using var factory = new AudioApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        using var retryRequest = CreateRequest(ValidRequest(), "client-key-123456");
        var originalResponse = await client.SendAsync(retryRequest);
        var original = await originalResponse.Content.ReadFromJsonAsync<AcceptedResponse>();

        using var exactRetry = CreateRequest(ValidRequest(), "client-key-123456");
        var retryResponse = await client.SendAsync(exactRetry);
        var retry = await retryResponse.Content.ReadFromJsonAsync<AcceptedResponse>();
        using var conflict = CreateRequest(ValidRequest(end: "00:00:45"), "client-key-123456");
        var conflictResponse = await client.SendAsync(conflict);

        Assert.Equal(HttpStatusCode.Accepted, originalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, retryResponse.StatusCode);
        Assert.NotNull(original);
        Assert.Equal(original.RequestId, retry?.RequestId);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal(1, factory.MetadataLookupCount);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.Single(await dbContext.Requests.ToListAsync());
        Assert.Single(await dbContext.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData("00:60:00", "01:00:00")]
    [InlineData("00:00:30", "00:00:30")]
    [InlineData("00:00:31", "00:00:30")]
    [InlineData("00:00:00", "00:30:01")]
    public async Task Invalid_interval_is_rejected_before_video_lookup(string start, string end)
    {
        await using var factory = new AudioApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync(
            "/api/audio-extractions",
            ValidRequest(start: start, end: end));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.MetadataLookupCount);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.Empty(await dbContext.Requests.ToListAsync());
        Assert.Empty(await dbContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Source_duration_over_limit_is_rejected_without_creating_work()
    {
        await using var factory = new AudioApiFactory { SourceDurationSeconds = 21_601 };
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync("/api/audio-extractions", ValidRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.Empty(await dbContext.Requests.ToListAsync());
    }

    [Fact]
    public async Task Metadata_service_failure_returns_503_without_accepting_request()
    {
        await using var factory = new AudioApiFactory { MetadataIsUnavailable = true };
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync("/api/audio-extractions", ValidRequest());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.Empty(await dbContext.Requests.ToListAsync());
        Assert.Empty(await dbContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Metadata_timeout_returns_503_without_accepting_request()
    {
        await using var factory = new AudioApiFactory { MetadataTimesOut = true };
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync("/api/audio-extractions", ValidRequest());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.Empty(await dbContext.Requests.ToListAsync());
    }

    [Fact]
    public async Task Rate_limit_returns_problem_response_after_five_submissions_from_one_ip()
    {
        await using var factory = new AudioApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        var responses = new List<HttpResponseMessage>();
        for (var index = 0; index < 6; index++)
        {
            responses.Add(await client.PostAsJsonAsync("/api/audio-extractions", ValidRequest()));
        }

        Assert.All(responses.Take(5), response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[5].StatusCode);
        Assert.Equal(
            "application/problem+json",
            responses[5].Content.Headers.ContentType?.MediaType);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
            Assert.Equal(5, await dbContext.Requests.CountAsync());
            Assert.Equal(5, await dbContext.OutboxMessages.CountAsync());
        }
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Missing_video_returns_404_and_does_not_create_work()
    {
        await using var factory = new AudioApiFactory { VideoIsMissing = true };
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync("/api/audio-extractions", ValidRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.Empty(await dbContext.Requests.ToListAsync());
    }

    [Fact]
    public async Task Database_write_failure_returns_503_not_a_success_shaped_response()
    {
        await using var factory = new AudioApiFactory();
        await factory.InitializeDatabaseAsync();
        await factory.SetDatabaseReadOnlyAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync("/api/audio-extractions", ValidRequest());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static object ValidRequest(
        string url = "https://www.youtube.com/watch?v=abcdefghijk",
        string start = "00:00:00",
        string end = "00:00:30") => new { url, start, end };

    private static HttpRequestMessage CreateRequest(object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/audio-extractions")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private sealed record AcceptedResponse(string RequestId, string Status, string Message);

    internal sealed class AudioApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public bool VideoIsMissing { get; init; }
        public bool MetadataIsUnavailable { get; init; }
        public bool MetadataTimesOut { get; init; }
        public int SourceDurationSeconds { get; init; } = 3_600;
        public int MetadataLookupCount { get; private set; }

        static AudioApiFactory()
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__AudioExtraction",
                "Host=localhost;Database=tests;Username=tests;Password=tests");
            Environment.SetEnvironmentVariable("AudioExtraction__YouTubeApiKey", "test-api-key");
            Environment.SetEnvironmentVariable("AudioExtraction__ApplyMigrationsOnStartup", "false");
            Environment.SetEnvironmentVariable("RabbitMq__HostName", "localhost");
            Environment.SetEnvironmentVariable("RabbitMq__Port", "5672");
            Environment.SetEnvironmentVariable("RabbitMq__UserName", "test");
            Environment.SetEnvironmentVariable("RabbitMq__Password", "test-password");
        }

        public AudioApiFactory()
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
            SQLitePCL.raw.FreezeProvider();
            _connection.Open();
        }

        public async Task InitializeDatabaseAsync()
        {
            using var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        public async Task SetDatabaseReadOnlyAsync()
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "PRAGMA query_only = ON";
            await command.ExecuteNonQueryAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:AudioExtraction"] = "Data Source=:memory:",
                    ["AudioExtraction:YouTubeApiKey"] = "test-api-key",
                    ["AudioExtraction:ApplyMigrationsOnStartup"] = "false",
                    ["AudioExtraction:StorageEndpoint"] = "http://localhost:9000",
                    ["AudioExtraction:StorageAccessKey"] = "test-access-key",
                    ["AudioExtraction:StorageSecretKey"] = "test-secret-key",
                    ["AudioExtraction:StorageBucket"] = "audio-extractions",
                    ["RabbitMq:HostName"] = "localhost",
                    ["RabbitMq:Port"] = "5672",
                    ["RabbitMq:UserName"] = "test",
                    ["RabbitMq:Password"] = "test-password",
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<AudioExtractionDbContext>();
                services.RemoveAll<DbContextOptions<AudioExtractionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AudioExtractionDbContext>>();
                services.AddDbContext<AudioExtractionDbContext>(options => options.UseSqlite(_connection));
                services.RemoveAll<IYouTubeVideoMetadataClient>();
                services.AddSingleton<IYouTubeVideoMetadataClient>(
                    new FakeMetadataClient(
                        VideoIsMissing,
                        MetadataIsUnavailable,
                        MetadataTimesOut,
                        SourceDurationSeconds,
                        () => MetadataLookupCount++));
                services.RemoveAll<IOutboxMessagePublisher>();
                services.AddSingleton<IOutboxMessagePublisher, NoOpOutboxPublisher>();
                foreach (var descriptor in services
                             .Where(descriptor =>
                                 descriptor.ServiceType == typeof(IHostedService) &&
                                 descriptor.ImplementationType is not null &&
                                 (descriptor.ImplementationType == typeof(Api.AudioExtractions.Persistence.OutboxPublisher) ||
                                  descriptor.ImplementationType == typeof(Api.AudioExtractions.Messaging.AudioExtractionOutcomeConsumer) ||
                                  descriptor.ImplementationType == typeof(Api.AudioExtractions.Persistence.ExpiredExtractionCleanupService)))
                             .ToArray())
                {
                    services.Remove(descriptor);
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _connection.Dispose();
            }
        }

        private sealed class FakeMetadataClient(
            bool videoIsMissing,
            bool metadataIsUnavailable,
            bool metadataTimesOut,
            int sourceDurationSeconds,
            Action recordLookup) : IYouTubeVideoMetadataClient
        {
            public Task<YouTubeVideoMetadata> GetVideoMetadataAsync(
                string videoId,
                CancellationToken cancellationToken)
            {
                recordLookup();
                if (videoIsMissing)
                {
                    throw new YouTubeVideoNotFoundException();
                }
                if (metadataIsUnavailable)
                {
                    throw new YouTubeMetadataUnavailableException();
                }
                if (metadataTimesOut)
                {
                    throw new TaskCanceledException("simulated metadata timeout");
                }

                return Task.FromResult(new YouTubeVideoMetadata(sourceDurationSeconds));
            }
        }

        private sealed class NoOpOutboxPublisher : IOutboxMessagePublisher
        {
            public Task PublishAsync(
                Api.AudioExtractions.Persistence.Entities.OutboxMessageEntity message,
                CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
