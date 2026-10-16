using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Api.AudioExtractions.Messaging;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.YouTube;
using Api.Settings.Configuration;
using Api.Settings.Contracts;
using Api.Settings.Persistence;
using Api.Settings.Security;
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

namespace AudioApi.Tests.Settings;

public sealed class SettingsTests
{
    private const string ValidKey = "AIzaSyTestKey-1234567890_abcdef";

    private static string NewMasterKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmSecretProtector NewProtector(string? masterKey = null) =>
        new(Options.Create(new SettingsOptions { EncryptionKey = masterKey ?? NewMasterKey() }));

    [Fact]
    public void Protector_round_trips_without_exposing_plaintext()
    {
        var protector = NewProtector();

        var protectedValue = protector.Protect("purpose", ValidKey);

        Assert.DoesNotContain(ValidKey, protectedValue);
        Assert.Equal(ValidKey, protector.Unprotect("purpose", protectedValue));
        Assert.NotEqual(protectedValue, protector.Protect("purpose", ValidKey));
    }

    [Fact]
    public void Protector_rejects_other_purpose_other_key_and_tampering()
    {
        var protector = NewProtector();
        var protectedValue = protector.Protect("purpose", ValidKey);

        Assert.Throws<SecretProtectionException>(() => protector.Unprotect("other", protectedValue));
        Assert.Throws<SecretProtectionException>(() => NewProtector().Unprotect("purpose", protectedValue));
        var tampered = Convert.FromBase64String(protectedValue);
        tampered[^1] ^= 1;
        Assert.Throws<SecretProtectionException>(
            () => protector.Unprotect("purpose", Convert.ToBase64String(tampered)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("AAAA")]
    public void Protector_requires_a_valid_256_bit_key(string masterKey) =>
        Assert.Throws<SecretProtectionException>(() => NewProtector(masterKey).Protect("purpose", ValidKey));

    [Fact]
    public async Task Put_stores_the_key_encrypted_and_never_returns_it()
    {
        await using var factory = new SettingsApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            "/api/settings/youtube-api-key",
            new SaveYouTubeApiKeyRequest($"  {ValidKey}  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ValidKey, body);
        var status = await client.GetFromJsonAsync<YouTubeApiKeyStatus>("/api/settings/youtube-api-key");
        Assert.True(status!.Configured);
        Assert.Equal("database", status.Source);
        Assert.NotNull(status.UpdatedAt);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<SettingsDbContext>()
            .Settings.SingleAsync();
        Assert.DoesNotContain(ValidKey, stored.ProtectedValue);
        var provider = factory.Services.GetRequiredService<IYouTubeApiKeyProvider>();
        Assert.Equal(ValidKey, await provider.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Put_replaces_the_existing_key()
    {
        await using var factory = new SettingsApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        await client.PutAsJsonAsync("/api/settings/youtube-api-key", new SaveYouTubeApiKeyRequest(ValidKey));
        await client.PutAsJsonAsync("/api/settings/youtube-api-key", new SaveYouTubeApiKeyRequest("another-key-0987654321"));

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<SettingsDbContext>().Settings.CountAsync());
        var provider = factory.Services.GetRequiredService<IYouTubeApiKeyProvider>();
        Assert.Equal("another-key-0987654321", await provider.GetApiKeyAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("contains spaces in the middle of it")]
    [InlineData("has/invalid/characters?key=1")]
    public async Task Put_rejects_invalid_keys(string? apiKey)
    {
        await using var factory = new SettingsApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            "/api/settings/youtube-api-key",
            new SaveYouTubeApiKeyRequest(apiKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<SettingsDbContext>().Settings.ToListAsync());
    }

    [Fact]
    public async Task Put_returns_503_when_the_encryption_key_is_not_configured()
    {
        await using var factory = new SettingsApiFactory(encryptionKey: null);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            "/api/settings/youtube-api-key",
            new SaveYouTubeApiKeyRequest(ValidKey));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<SettingsDbContext>().Settings.ToListAsync());
    }

    [Fact]
    public async Task Database_key_takes_precedence_over_environment_and_delete_restores_it()
    {
        await using var factory = new SettingsApiFactory(environmentKey: "environment-key-1234567890");
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();
        var provider = factory.Services.GetRequiredService<IYouTubeApiKeyProvider>();

        var initial = await client.GetFromJsonAsync<YouTubeApiKeyStatus>("/api/settings/youtube-api-key");
        Assert.Equal(new YouTubeApiKeyStatus(true, "environment", null), initial);
        Assert.Equal("environment-key-1234567890", await provider.GetApiKeyAsync(CancellationToken.None));

        await client.PutAsJsonAsync("/api/settings/youtube-api-key", new SaveYouTubeApiKeyRequest(ValidKey));
        Assert.Equal(ValidKey, await provider.GetApiKeyAsync(CancellationToken.None));

        var deleted = await client.DeleteAsync("/api/settings/youtube-api-key");
        var status = await deleted.Content.ReadFromJsonAsync<YouTubeApiKeyStatus>();
        Assert.Equal("environment", status!.Source);
        Assert.Equal("environment-key-1234567890", await provider.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Status_reports_not_configured_when_no_key_exists()
    {
        await using var factory = new SettingsApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        var status = await client.GetFromJsonAsync<YouTubeApiKeyStatus>("/api/settings/youtube-api-key");

        Assert.Equal(new YouTubeApiKeyStatus(false, "none", null), status);
        var provider = factory.Services.GetRequiredService<IYouTubeApiKeyProvider>();
        Assert.Null(await provider.GetApiKeyAsync(CancellationToken.None));
    }

    private sealed class SettingsApiFactory(
        string? encryptionKey = "generate",
        string? environmentKey = null) : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly string? _encryptionKey = encryptionKey == "generate" ? NewMasterKey() : encryptionKey;

        static SettingsApiFactory()
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__AudioExtraction",
                "Host=localhost;Database=tests;Username=tests;Password=tests");
        }

        public HttpClient CreateHttpsClient() =>
            CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        public async Task InitializeDatabaseAsync()
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
            SQLitePCL.raw.FreezeProvider();
            _connection.Open();
            using var client = CreateHttpsClient();
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SettingsDbContext>().Database.EnsureCreatedAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:AudioExtraction"] = "Data Source=:memory:",
                    ["AudioExtraction:YouTubeApiKey"] = environmentKey,
                    ["AudioExtraction:ApplyMigrationsOnStartup"] = "false",
                    ["Grammar:ApplyMigrationsOnStartup"] = "false",
                    ["Settings:ApplyMigrationsOnStartup"] = "false",
                    ["Settings:EncryptionKey"] = _encryptionKey,
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
                services.RemoveAll<SettingsDbContext>();
                services.RemoveAll<DbContextOptions<SettingsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<SettingsDbContext>>();
                services.AddDbContext<SettingsDbContext>(options => options.UseSqlite(_connection));
                services.RemoveAll<IOutboxMessagePublisher>();
                foreach (var descriptor in services
                             .Where(descriptor =>
                                 descriptor.ServiceType == typeof(IHostedService) &&
                                 descriptor.ImplementationType is not null &&
                                 (descriptor.ImplementationType == typeof(OutboxPublisher) ||
                                  descriptor.ImplementationType == typeof(AudioExtractionOutcomeConsumer) ||
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
}
