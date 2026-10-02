using Api.AudioExtractions.Validation;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class ExtractionIntervalValidatorTests
{
    [Fact]
    public void TryParseTimecode_parses_hours_beyond_24()
    {
        Assert.True(ExtractionIntervalValidator.TryParseTimecode("27:05:09", out var seconds));
        Assert.Equal(97_509, seconds);
    }

    [Theory]
    [InlineData("1:02:03")]
    [InlineData("01:60:00")]
    [InlineData("01:00:60")]
    [InlineData("-1:00:00")]
    [InlineData("01:02:03.5")]
    [InlineData("999999999999999999999999:00:00")]
    public void TryParseTimecode_rejects_invalid_or_overflowing_values(string value)
    {
        Assert.False(ExtractionIntervalValidator.TryParseTimecode(value, out _));
    }

    [Fact]
    public void Validate_accepts_interval_within_source_and_configured_limits()
    {
        var result = ExtractionIntervalValidator.Validate("00:05:30", "00:08:45", 3_600, 1_800, 21_600);

        Assert.True(result.IsValid);
        Assert.Equal(330, result.StartSeconds);
        Assert.Equal(525, result.EndSeconds);
    }

    [Theory]
    [InlineData("00:10:00", "00:10:00", 3_600)]
    [InlineData("00:10:01", "00:10:00", 3_600)]
    [InlineData("00:00:00", "00:30:01", 3_600)]
    [InlineData("00:59:00", "01:01:00", 3_600)]
    [InlineData("00:00:00", "00:01:00", 21_601)]
    public void Validate_rejects_equal_reversed_oversized_and_out_of_duration_ranges(
        string start,
        string end,
        int sourceDuration)
    {
        var result = ExtractionIntervalValidator.Validate(start, end, sourceDuration, 1_800, 21_600);

        Assert.False(result.IsValid);
    }
}
