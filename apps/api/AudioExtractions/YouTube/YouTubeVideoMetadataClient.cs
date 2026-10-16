using System.Globalization;
using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Api.AudioExtractions.Configuration;

namespace Api.AudioExtractions.YouTube;

public sealed record YouTubeVideoMetadata(int DurationSeconds);

public sealed class YouTubeVideoNotFoundException : Exception
{
}

public sealed class YouTubeMetadataUnavailableException(Exception? innerException = null)
    : Exception("YouTube metadata is temporarily unavailable.", innerException);

public interface IYouTubeVideoMetadataClient
{
    Task<YouTubeVideoMetadata> GetVideoMetadataAsync(string videoId, CancellationToken cancellationToken);
}

public sealed class YouTubeVideoMetadataClient(
    HttpClient httpClient,
    IOptions<AudioExtractionOptions> options,
    ILogger<YouTubeVideoMetadataClient> logger) : IYouTubeVideoMetadataClient
{
    public async Task<YouTubeVideoMetadata> GetVideoMetadataAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString(
            "https://www.googleapis.com/youtube/v3/videos",
            new Dictionary<string, string?>
            {
                ["part"] = "contentDetails",
                ["id"] = videoId,
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", options.Value.YouTubeApiKey);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(
                "YouTube metadata request failed before a response ({ExceptionType}, inner {InnerExceptionType}).",
                exception.GetType().Name,
                exception.InnerException?.GetType().Name);
            throw;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var reason = await ReadErrorReasonAsync(response, cancellationToken);
                logger.LogError(
                    "YouTube metadata request failed with HTTP {StatusCode} and reason {Reason}.",
                    (int)response.StatusCode,
                    reason);
                throw new YouTubeMetadataUnavailableException();
            }

            return await ParseMetadataAsync(response, cancellationToken);
        }
    }

    private static async Task<string> ReadErrorReasonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            var error = document.RootElement.GetProperty("error");
            if (error.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0 &&
                errors[0].TryGetProperty("reason", out var reason) &&
                reason.GetString() is { Length: <= 100 } value)
            {
                return value;
            }

            return error.TryGetProperty("status", out var status) && status.GetString() is { Length: <= 100 } text
                ? text
                : "unknown";
        }
        catch (Exception exception) when (
            exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "unparseable";
        }
    }

    private static async Task<YouTubeVideoMetadata> ParseMetadataAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array ||
                items.GetArrayLength() == 0)
            {
                throw new YouTubeVideoNotFoundException();
            }

            var duration = items[0]
                .GetProperty("contentDetails")
                .GetProperty("duration")
                .GetString();
            if (duration is null)
            {
                throw new YouTubeMetadataUnavailableException();
            }

            var durationSeconds = (int)Math.Floor(XmlConvert.ToTimeSpan(duration).TotalSeconds);
            if (durationSeconds <= 0)
            {
                throw new YouTubeVideoNotFoundException();
            }

            return new YouTubeVideoMetadata(durationSeconds);
        }
        catch (YouTubeVideoNotFoundException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or XmlException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new YouTubeMetadataUnavailableException(exception);
        }
    }
}
