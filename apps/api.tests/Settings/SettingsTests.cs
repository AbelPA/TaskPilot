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
using Api.Settings.Services;
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

    [Theory]
    [InlineData("AIzaSyTestKey-1234567890_abcdef", "AIzaS*********def")]
    [InlineData("abcdefghij", "ab*********ij")]
    public void MaskApiKey_keeps_only_the_edges(string key, string expected) =>
        Assert.Equal(expected, SecretMasking.MaskApiKey(key));

    [Theory]
    [InlineData("tpedropa@gmail.com", "tp***pa@gmail.com")]
    [InlineData("ab@x.com", "a***@x.com")]
    public void MaskUsername_keeps_the_domain(string username, string expected) =>
        Assert.Equal(expected, SecretMasking.MaskUsername(username));

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
        Assert.Equal(SecretMasking.MaskApiKey(ValidKey), status.MaskedValue);
        Assert.NotEqual(ValidKey, status.MaskedValue);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<SettingsDbContext>()
            .Settings.SingleAsync();
        Assert.DoesNotContain(ValidKey, stored.ProtectedValue);
        var provider = factory.Services.GetRequiredService<IYouTubeApiKeyProvider>();
        Assert.Equal(ValidKey, await provider.GetApiKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Youtube_account_is_stored_encrypted_and_password_is_never_returned()
    {
        await using var factory = new SettingsApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            "/api/settings/youtube-account",
            new SaveYouTubeAccountRequest(" worker@example.com ", "s3cret-Password!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("s3cret-Password!", await response.Content.ReadAsStringAsync());
        var status = await client.GetFromJsonAsync<YouTubeAccountStatus>("/api/settings/youtube-account");
        Assert.True(status!.Configured);
        Assert.Equal("wo***er@example.com", status.MaskedUsername);
        Assert.Equal(SecretMasking.MaskedPassword, status.MaskedPassword);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<SettingsDbContext>().Settings.SingleAsync();
        Assert.DoesNotContain("s3cret-Password!", stored.ProtectedValue);
        Assert.DoesNotContain("worker@example.com", stored.ProtectedValue);

        var removed = await client.DeleteFromJsonAsync<YouTubeAccountStatus>("/api/settings/youtube-account");
        Assert.False(removed!.Configured);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("user@example.com", "")]
    public async Task Youtube_account_rejects_missing_fields(string username, string password)
    {
        await using var factory = new SettingsApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync(
            "/api/settings/youtube-account",
            new SaveYouTubeAccountRequest(username, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Internal_youtube_account_requires_the_token_and_returns_credentials()
    {
        await using var factory = new SettingsApiFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/internal/youtube-account")).StatusCode);
        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/internal/youtube-account");
        wrong.Headers.Add("X-Internal-Token", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);

        using var empty = new HttpRequestMessage(HttpMethod.Get, "/internal/youtube-account");
        empty.Headers.Add("X-Internal-Token", "internal-test-token");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(empty)).StatusCode);

        await client.PutAsJsonAsync(
            "/api/settings/youtube-account",
            new SaveYouTubeAccountRequest("worker@example.com", "s3cret-Password!"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/youtube-account");
        request.Headers.Add("X-Internal-Token", "internal-test-token");
        var credentials = await (await client.SendAsync(request)).Content
            .ReadFromJsonAsync<YouTubeAccountCredentials>();
        Assert.Equal("worker@example.com", credentials!.Username);
        Assert.Equal("s3cret-Password!", credentials.Password);
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
        Assert.Equal(
            new YouTubeApiKeyStatus(true, "environment", null, SecretMasking.MaskApiKey("environment-key-1234567890")),
            initial);
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
                    ["Settings:InternalToken"] = "internal-test-token",
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
