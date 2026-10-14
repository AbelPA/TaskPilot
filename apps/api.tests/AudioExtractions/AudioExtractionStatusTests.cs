using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class AudioExtractionStatusTests
{
    private const string AcceptedId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string CompletedId = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string FailedId = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private const string ExpiredId = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";

    [Fact]
    public async Task Status_endpoint_returns_accepted_completed_and_failed_persisted_states()
    {
        await using var factory = new AudioExtractionEndpointTests.AudioApiFactory();
        await factory.InitializeDatabaseAsync();
        await SeedRequestsAsync(factory);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var accepted = await client.GetFromJsonAsync<StatusResponse>(
            $"/api/audio-extractions/{AcceptedId}");
        var completedResponse = await client.GetAsync($"/api/audio-extractions/{CompletedId}");
        var completed = await completedResponse.Content.ReadFromJsonAsync<StatusResponse>();
        var failed = await client.GetFromJsonAsync<StatusResponse>(
            $"/api/audio-extractions/{FailedId}");

        Assert.Equal("accepted", accepted!.Status);
        Assert.Null(accepted.Result);
        Assert.Equal(HttpStatusCode.OK, completedResponse.StatusCode);
        Assert.Equal("no-store", completedResponse.Headers.CacheControl?.ToString());
        Assert.Equal("completed", completed!.Status);
        Assert.Equal("audio/mpeg", completed.Result!.ContentType);
        Assert.Equal($"/api/audio-extractions/{CompletedId}/audio", completed.Result.AudioPath);
        Assert.Equal("failed", failed!.Status);
        Assert.Equal("O vídeo não está disponível para processamento.", failed.Message);
    }

    [Fact]
    public async Task Malformed_unknown_and_expired_capabilities_have_the_same_not_found_shape()
    {
        await using var factory = new AudioExtractionEndpointTests.AudioApiFactory();
        await factory.InitializeDatabaseAsync();
        await SeedRequestsAsync(factory);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var malformed = await client.GetAsync("/api/audio-extractions/not-a-capability");
        var unknown = await client.GetAsync(
            "/api/audio-extractions/EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE");
        var expired = await client.GetAsync($"/api/audio-extractions/{ExpiredId}");

        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        using var malformedBody = JsonDocument.Parse(await malformed.Content.ReadAsStringAsync());
        using var unknownBody = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        using var expiredBody = JsonDocument.Parse(await expired.Content.ReadAsStringAsync());
        foreach (var field in new[] { "title", "detail", "status", "code" })
        {
            Assert.Equal(
                malformedBody.RootElement.GetProperty(field).ToString(),
                unknownBody.RootElement.GetProperty(field).ToString());
            Assert.Equal(
                malformedBody.RootElement.GetProperty(field).ToString(),
                expiredBody.RootElement.GetProperty(field).ToString());
        }
        Assert.Equal("application/problem+json", malformed.Content.Headers.ContentType?.MediaType);
    }

    private static async Task SeedRequestsAsync(
        AudioExtractionEndpointTests.AudioApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        var now = DateTimeOffset.UtcNow;
        dbContext.Requests.AddRange(
            CreateRequest(AcceptedId, "accepted", now),
            CreateRequest(CompletedId, "completed", now),
            CreateRequest(FailedId, "failed", now),
            CreateRequest(ExpiredId, "completed", now.AddDays(-8), now.AddDays(-1)));
        dbContext.NotificationOutcomes.AddRange(
            new NotificationOutcomeEntity
            {
                RequestId = CompletedId,
                EventId = Guid.NewGuid(),
                Status = "completed",
                SafeMessage = "A extração de áudio solicitada foi concluída.",
                CreatedAt = now,
            },
            new NotificationOutcomeEntity
            {
                RequestId = FailedId,
                EventId = Guid.NewGuid(),
                Status = "failed",
                SafeMessage = "O vídeo não está disponível para processamento.",
                CreatedAt = now,
            },
            new NotificationOutcomeEntity
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
        else if (status == "failed")
        {
            request.FailureCode = "VIDEO_UNAVAILABLE";
            request.FailureMessage = "O vídeo não está disponível para processamento.";
        }

        return request;
    }

    private sealed record StatusResponse(
        string RequestId,
        string Status,
        string Message,
        DateTimeOffset CreatedAt,
        ResultResponse? Result);

    private sealed record ResultResponse(string AudioPath, int DurationSeconds, string ContentType);
}
