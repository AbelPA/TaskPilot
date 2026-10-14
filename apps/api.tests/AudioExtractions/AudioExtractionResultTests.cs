using System.Net;
using System.Text.Json;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Persistence.Entities;
using Api.AudioExtractions.Services;
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
using Microsoft.Extensions.Options;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class AudioExtractionResultTests
{
    private const string CompletedId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string UnknownId = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string AcceptedId = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private const string FailedId = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";
    private const string ExpiredId = "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE";

    [Fact]
    public async Task Completed_result_redirects_to_a_short_lived_private_reference_without_caching_or_referrer()
    {
        await using var factory = new ResultApiFactory();
        await factory.InitializeDatabaseAsync();
        await SeedRequestsAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync($"/api/audio-extractions/{CompletedId}/audio");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(
            $"https://storage.example/private/audio-extractions/{CompletedId}/audio.mp3?signature=test",
            response.Headers.Location?.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, factory.ObjectStore.SignRequestCount);
        Assert.Equal($"audio-extractions/{CompletedId}/audio.mp3", factory.ObjectStore.SignedObjectKey);
        Assert.Equal(300, factory.ObjectStore.SignedUrlTtlSeconds);
        Assert.False(factory.ObjectStore.DownloadRequested);
    }

    [Fact]
    public async Task Download_request_uses_attachment_disposition_and_never_outlives_result_retention()
    {
        await using var factory = new ResultApiFactory();
        await factory.InitializeDatabaseAsync();
        var nearExpiryId = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";
        await SeedRequestsAsync(factory, nearExpiryId);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(
            $"/api/audio-extractions/{nearExpiryId}/audio?download=true");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.True(factory.ObjectStore.DownloadRequested);
        Assert.Equal(
            $"audio-extractions/{nearExpiryId}/audio.mp3",
            factory.ObjectStore.SignedObjectKey);
        Assert.InRange(factory.ObjectStore.SignedUrlTtlSeconds, 1, 120);
    }

    [Fact]
    public async Task Minio_signing_uses_the_public_endpoint_and_attachment_response_override()
    {
        var objectStore = new MinioAudioObjectStore(Options.Create(new Api.AudioExtractions.Configuration.AudioExtractionOptions
        {
            StorageEndpoint = "https://audio.example.test",
            StorageManagementEndpoint = "http://minio:9000",
            StorageAccessKey = "test-access-key",
            StorageSecretKey = "test-secret-key",
            StorageBucket = "audio-extractions",
        }));

        var signedUrl = await objectStore.CreatePresignedGetUrlAsync(
            $"audio-extractions/{CompletedId}/audio.mp3",
            60,
            true,
            CancellationToken.None);
        var signedUri = new Uri(signedUrl);
        var query = Uri.UnescapeDataString(signedUri.Query);

        Assert.Equal("audio.example.test", signedUri.Host);
        Assert.Contains("response-content-disposition", query);
        Assert.Contains("attachment; filename=\"audio.mp3\"", query);
    }

    [Fact]
    public async Task Temporary_object_store_failure_returns_a_safe_service_unavailable_problem()
    {
        await using var factory = new ResultApiFactory();
        await factory.InitializeDatabaseAsync();
        await SeedRequestsAsync(factory);
        factory.ObjectStore.FailSigning = true;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync($"/api/audio-extractions/{CompletedId}/audio");
        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("AUDIO_RESULT_UNAVAILABLE", responseJson.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("storage.example", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("not-a-capability")]
    [InlineData(UnknownId)]
    [InlineData(AcceptedId)]
    [InlineData(FailedId)]
    [InlineData(ExpiredId)]
    public async Task Invalid_unavailable_or_expired_results_share_the_same_not_found_response(string requestId)
    {
        await using var factory = new ResultApiFactory();
        await factory.InitializeDatabaseAsync();
        await SeedRequestsAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync($"/api/audio-extractions/{requestId}/audio");
        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("Audio extraction not found", responseJson.RootElement.GetProperty("title").GetString());
        Assert.Equal("A solicitação não foi encontrada.", responseJson.RootElement.GetProperty("detail").GetString());
        Assert.Equal("AUDIO_EXTRACTION_NOT_FOUND", responseJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, factory.ObjectStore.SignRequestCount);
    }

    [Fact]
    public async Task Cleanup_deletes_expired_objects_and_records_but_retains_pending_requests()
    {
        await using var factory = new ResultApiFactory();
        await factory.InitializeDatabaseAsync();
        await SeedRequestsAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<ExpiredExtractionCleanupService>();

        await cleanup.CleanupExpiredAsync(CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.False(await dbContext.Requests.AnyAsync(request => request.RequestId == ExpiredId));
        Assert.False(await dbContext.NotificationOutcomes.AnyAsync(outcome => outcome.RequestId == ExpiredId));
        Assert.True(await dbContext.Requests.AnyAsync(request => request.RequestId == AcceptedId));
        Assert.Equal(
            new[] { $"audio-extractions/{ExpiredId}/audio.mp3" },
            factory.ObjectStore.DeletedObjectKeys);
    }

    [Fact]
    public async Task Cleanup_retains_expired_records_when_artifact_deletion_fails()
    {
        await using var factory = new ResultApiFactory();
        await factory.InitializeDatabaseAsync();
        await SeedRequestsAsync(factory);
        factory.ObjectStore.FailDelete = true;
        await using var scope = factory.Services.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<ExpiredExtractionCleanupService>();

        await cleanup.CleanupExpiredAsync(CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        Assert.True(await dbContext.Requests.AnyAsync(request => request.RequestId == ExpiredId));
        Assert.True(await dbContext.NotificationOutcomes.AnyAsync(outcome => outcome.RequestId == ExpiredId));
        Assert.Empty(factory.ObjectStore.DeletedObjectKeys);
    }

    private static async Task SeedRequestsAsync(
        ResultApiFactory factory,
        string? nearExpiryId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        var now = DateTimeOffset.UtcNow;
        var requests = new List<AudioExtractionRequestEntity>
        {
            CreateRequest(CompletedId, "completed", now.AddDays(1)),
            CreateRequest(AcceptedId, "accepted", null),
            CreateRequest(FailedId, "failed", now.AddDays(1)),
            CreateRequest(ExpiredId, "completed", now.AddSeconds(-1)),
        };
        if (nearExpiryId is not null)
        {
            requests.Add(CreateRequest(nearExpiryId, "completed", now.AddMinutes(2)));
        }
        dbContext.Requests.AddRange(requests);
        dbContext.NotificationOutcomes.Add(new NotificationOutcomeEntity
        {
            RequestId = ExpiredId,
            EventId = Guid.NewGuid(),
            Status = "completed",
            SafeMessage = "A extração de áudio solicitada foi concluída.",
            CreatedAt = now.AddDays(-8),
        });
        await dbContext.SaveChangesAsync();
    }

    private static AudioExtractionRequestEntity CreateRequest(
        string requestId,
        string status,
        DateTimeOffset? expiresAt)
    {
        var request = new AudioExtractionRequestEntity
        {
            RequestId = requestId,
            VideoId = "abcdefghijk",
            StartSeconds = 0,
            EndSeconds = 30,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            SourceDurationSeconds = 60,
            ExpiresAt = expiresAt,
        };
        if (status == "completed")
        {
            request.ResultObjectKey = $"audio-extractions/{requestId}/audio.mp3";
            request.ResultSizeBytes = 50;
            request.ResultChecksumSha256 = new string('a', 64);
            request.ResultDurationSeconds = 30;
        }
        else if (status == "failed")
        {
            request.FailureCode = "VIDEO_UNAVAILABLE";
            request.FailureMessage = "O vídeo não está disponível para processamento.";
        }

        return request;
    }

    private sealed class ResultApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public FakeAudioObjectStore ObjectStore { get; } = new();

        static ResultApiFactory()
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__AudioExtraction",
                "Host=localhost;Database=tests;Username=tests;******");
            Environment.SetEnvironmentVariable("AudioExtraction__YouTubeApiKey", "test-api-key");
            Environment.SetEnvironmentVariable("AudioExtraction__ApplyMigrationsOnStartup", "false");
            Environment.SetEnvironmentVariable("AudioExtraction__StorageEndpoint", "http://localhost:9000");
            Environment.SetEnvironmentVariable("AudioExtraction__StorageAccessKey", "test-access-key");
            Environment.SetEnvironmentVariable("AudioExtraction__StorageSecretKey", "test-secret-key");
            Environment.SetEnvironmentVariable("AudioExtraction__StorageBucket", "audio-extractions");
            Environment.SetEnvironmentVariable("RabbitMq__HostName", "localhost");
            Environment.SetEnvironmentVariable("RabbitMq__Port", "5672");
            Environment.SetEnvironmentVariable("RabbitMq__UserName", "test");
            Environment.SetEnvironmentVariable("RabbitMq__Password", "test-password");
        }

        public ResultApiFactory()
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
                services.RemoveAll<IAudioObjectStore>();
                services.AddSingleton<IAudioObjectStore>(ObjectStore);
                foreach (var descriptor in services
                             .Where(descriptor =>
                                 descriptor.ServiceType == typeof(IHostedService) &&
                                 descriptor.ImplementationType is not null &&
                                 (descriptor.ImplementationType == typeof(Api.AudioExtractions.Persistence.OutboxPublisher) ||
                                  descriptor.ImplementationType == typeof(Api.AudioExtractions.Messaging.AudioExtractionOutcomeConsumer) ||
                                  descriptor.ImplementationType == typeof(ExpiredExtractionCleanupService)))
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
    }

    private sealed class FakeAudioObjectStore : IAudioObjectStore
    {
        public int SignRequestCount { get; private set; }
        public int SignedUrlTtlSeconds { get; private set; }
        public bool DownloadRequested { get; private set; }
        public bool FailSigning { get; set; }
        public bool FailDelete { get; set; }
        public string? SignedObjectKey { get; private set; }
        public List<string> DeletedObjectKeys { get; } = [];

        public Task<string> CreatePresignedGetUrlAsync(
            string objectKey,
            int expirationSeconds,
            bool asDownload,
            CancellationToken cancellationToken)
        {
            SignRequestCount++;
            if (FailSigning)
            {
                throw new AudioObjectStoreException("Storage credentials must not appear in the response.");
            }
            SignedObjectKey = objectKey;
            SignedUrlTtlSeconds = expirationSeconds;
            DownloadRequested = asDownload;
            return Task.FromResult(
                $"https://storage.example/private/{objectKey}?signature=test");
        }

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            if (FailDelete)
            {
                throw new AudioObjectStoreException("Object deletion failed.");
            }
            DeletedObjectKeys.Add(objectKey);
            return Task.CompletedTask;
        }
    }
}
