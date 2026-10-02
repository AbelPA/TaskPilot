using Api.AudioExtractions.Validation;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class YouTubeVideoUrlParserTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk", "abcdefghijk")]
    [InlineData("https://youtu.be/abcdefghijk", "abcdefghijk")]
    [InlineData("https://m.youtube.com/shorts/abcdefghijk", "abcdefghijk")]
    [InlineData("https://www.youtube-nocookie.com/embed/abcdefghijk", "abcdefghijk")]
    public void TryParse_accepts_supported_hosts_and_paths(string url, string expectedId)
    {
        Assert.True(YouTubeVideoUrlParser.TryParse(url, out var videoId));
        Assert.Equal(expectedId, videoId);
    }

    [Theory]
    [InlineData("http://www.youtube.com/watch?v=abcdefghijk")]
    [InlineData("https://youtube.com.evil.test/watch?v=abcdefghijk")]
    [InlineData("https://evil.test/watch?v=abcdefghijk")]
    [InlineData("https://www.youtube.com:8443/watch?v=abcdefghijk")]
    [InlineData("https://user@www.youtube.com/watch?v=abcdefghijk")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk&v=lmnopqrstuv")]
    [InlineData("https://www.youtube.com/redirect?url=https://example.com")]
    public void TryParse_rejects_unsupported_or_ambiguous_urls(string url)
    {
        Assert.False(YouTubeVideoUrlParser.TryParse(url, out _));
    }
}
