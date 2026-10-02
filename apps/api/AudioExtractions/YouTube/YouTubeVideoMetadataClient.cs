using System.Globalization;
using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.WebUtilities;
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
    IOptions<AudioExtractionOptions> options) : IYouTubeVideoMetadataClient
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
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new YouTubeMetadataUnavailableException();
        }

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
