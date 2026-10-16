using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class AudioExtractionNotificationStreamTests
{
    private const string AcceptedId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string CompletedId = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string ExpiredId = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    [Fact]
    public async Task Registration_rejects_empty_sets_and_hides_invalid_capabilities()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        var empty = await client.PostAsJsonAsync(
            "/api/audio-extractions/notification-streams",
            new { requestIds = Array.Empty<string>() });
        var malformed = await client.PostAsJsonAsync(
            "/api/audio-extractions/notification-streams",
            new { requestIds = new[] { "not-a-capability" } });
        var mixed = await client.PostAsJsonAsync(
            "/api/audio-extractions/notification-streams",
            new { requestIds = new[] { AcceptedId, ExpiredId } });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mixed.StatusCode);
        using var malformedBody = JsonDocument.Parse(await malformed.Content.ReadAsStringAsync());
        using var mixedBody = JsonDocument.Parse(await mixed.Content.ReadAsStringAsync());
        Assert.Equal(
            malformedBody.RootElement.GetProperty("title").GetString(),
            mixedBody.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            malformedBody.RootElement.GetProperty("detail").GetString(),
            mixedBody.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain(ExpiredId, await mixed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registration_deduplicates_ids_and_stream_replays_persisted_terminal_outcome()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);

        var registration = await client.PostAsJsonAsync(
            "/api/audio-extractions/notification-streams",
            new { requestIds = new[] { CompletedId, CompletedId } });
        var body = await registration.Content.ReadFromJsonAsync<RegistrationResponse>();

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal("no-store", registration.Headers.CacheControl?.ToString());
        Assert.NotNull(body);
        Assert.StartsWith(
            "/api/audio-extractions/notification-streams/",
            body.StreamUrl,
            StringComparison.Ordinal);

        using var response = await client.GetAsync(body.StreamUrl);
        var streamText = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("no", response.Headers.GetValues("X-Accel-Buffering").Single());
        Assert.Contains("retry: 3000\n", streamText, StringComparison.Ordinal);
        Assert.Contains("event: extractionUpdated\n", streamText, StringComparison.Ordinal);
        Assert.Contains($"\"requestId\":\"{CompletedId}\"", streamText, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", streamText, StringComparison.Ordinal);
        Assert.DoesNotContain(AcceptedId, streamText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stream_revalidates_capabilities_on_every_connection()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var registration = await client.PostAsJsonAsync(
            "/api/audio-extractions/notification-streams",
            new { requestIds = new[] { CompletedId } });
        var body = await registration.Content.ReadFromJsonAsync<RegistrationResponse>();
        Assert.NotNull(body);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
            var request = await dbContext.Requests.SingleAsync(item => item.RequestId == CompletedId);
            request.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.GetAsync(body.StreamUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stream_remains_open_for_active_requests_until_client_cancels()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = CreateClient(factory);
        var registration = await client.PostAsJsonAsync(
            "/api/audio-extractions/notification-streams",
            new { requestIds = new[] { AcceptedId } });
        var body = await registration.Content.ReadFromJsonAsync<RegistrationResponse>();
        Assert.NotNull(body);

        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Get, body.StreamUrl);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellation.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        cancellation.Cancel();
        var buffer = new byte[1];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await stream.ReadExactlyAsync(buffer, cancellation.Token));
    }

    private static async Task<AudioExtractionEndpointTests.AudioApiFactory> CreateFactoryAsync()
    {
        var factory = new AudioExtractionEndpointTests.AudioApiFactory();
        await factory.InitializeDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        var now = DateTimeOffset.UtcNow;
        dbContext.Requests.AddRange(
            CreateRequest(AcceptedId, "accepted", now),
            CreateRequest(CompletedId, "completed", now),
            CreateRequest(ExpiredId, "completed", now, now.AddMinutes(-1)));
        dbContext.NotificationOutcomes.AddRange(
            CreateOutcome(CompletedId, now),
            CreateOutcome(ExpiredId, now.AddDays(-1)));
        await dbContext.SaveChangesAsync();
        return factory;
    }

    private static AudioExtractionRequestEntity CreateRequest(
        string requestId,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null)
    {
        var request = new AudioExtractionRequestEntity
        {
            RequestId = requestId,
            VideoId = "abcdefghijk",
            StartSeconds = 0,
            EndSeconds = 30,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            SourceDurationSeconds = 60,
            ExpiresAt = status == "accepted" ? null : expiresAt ?? createdAt.AddDays(7),
        };
        if (status == "completed")
        {
            request.ResultObjectKey = $"audio-extractions/{requestId}/audio.mp3";
            request.ResultSizeBytes = 50;
            request.ResultChecksumSha256 = new string('a', 64);
            request.ResultDurationSeconds = 30;
        }

        return request;
    }

    private static NotificationOutcomeEntity CreateOutcome(string requestId, DateTimeOffset createdAt) =>
        new()
        {
            RequestId = requestId,
            EventId = Guid.NewGuid(),
            Status = "completed",
            SafeMessage = "A extração de áudio solicitada foi concluída.",
            CreatedAt = createdAt,
        };

    private static HttpClient CreateClient(AudioExtractionEndpointTests.AudioApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed record RegistrationResponse(string StreamUrl);
}
