using System.Net;
using System.Text;
using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.YouTube;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class YouTubeVideoMetadataClientTests
{
    [Fact]
    public async Task Requests_metadata_only_from_fixed_google_host_with_key_in_header()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient(
            (request, _) =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"items":[{"contentDetails":{"duration":"PT1H2M3S"}}]}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            },
            out var httpClient);
        using (httpClient)

        {
            var metadata = await client.GetVideoMetadataAsync("abcdefghijk", CancellationToken.None);

            Assert.Equal(3_723, metadata.DurationSeconds);
            Assert.Equal("www.googleapis.com", capturedRequest!.RequestUri!.Host);
            Assert.Equal("/youtube/v3/videos", capturedRequest.RequestUri.AbsolutePath);
            Assert.Contains("id=abcdefghijk", capturedRequest.RequestUri.Query);
            Assert.DoesNotContain("api-key", capturedRequest.RequestUri.Query);
            Assert.Equal("test-api-key", capturedRequest.Headers.GetValues("X-Goog-Api-Key").Single());
        }
    }

    [Fact]
    public async Task Empty_video_result_is_reported_as_not_found()
    {
        var client = CreateClient(
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json"),
            },
            out var httpClient);
        using (httpClient)
        {
            await Assert.ThrowsAsync<YouTubeVideoNotFoundException>(() =>
                client.GetVideoMetadataAsync("abcdefghijk", CancellationToken.None));
        }
    }

    [Fact]
    public async Task Upstream_failure_is_reported_without_provider_details()
    {
        var client = CreateClient(
            (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            out var httpClient);
        using (httpClient)
        {
            var exception = await Assert.ThrowsAsync<YouTubeMetadataUnavailableException>(() =>
                client.GetVideoMetadataAsync("abcdefghijk", CancellationToken.None));
            Assert.DoesNotContain("key", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Upstream_failure_logs_status_and_reason_without_key_or_message()
    {
        var logger = new CapturingLogger();
        var client = CreateClient(
            (_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    """{"error":{"code":403,"message":"secret details","errors":[{"reason":"quotaExceeded"}]}}""",
                    Encoding.UTF8,
                    "application/json"),
            },
            out var httpClient,
            logger);
        using (httpClient)
        {
            await Assert.ThrowsAsync<YouTubeMetadataUnavailableException>(() =>
                client.GetVideoMetadataAsync("abcdefghijk", CancellationToken.None));
        }

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("403", entry);
        Assert.Contains("quotaExceeded", entry);
        Assert.DoesNotContain("test-api-key", entry);
        Assert.DoesNotContain("secret details", entry);
    }

    private sealed class CapturingLogger : ILogger<YouTubeVideoMetadataClient>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
    }

    private static YouTubeVideoMetadataClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory,
        out HttpClient httpClient,
        ILogger<YouTubeVideoMetadataClient>? logger = null)
    {
        httpClient = new HttpClient(new StubHandler(responseFactory));
        return new YouTubeVideoMetadataClient(httpClient, Options.Create(new AudioExtractionOptions
        {
            YouTubeApiKey = "test-api-key",
        }), logger ?? NullLogger<YouTubeVideoMetadataClient>.Instance);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request, cancellationToken));
    }
}
