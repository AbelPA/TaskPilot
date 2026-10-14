using System.Globalization;

namespace Api.AudioExtractions.Validation;

public readonly record struct IntervalValidationResult(
    bool IsValid,
    int StartSeconds,
    int EndSeconds,
    string? Error)
{
    public static IntervalValidationResult Invalid(string error) => new(false, 0, 0, error);
}

public static class ExtractionIntervalValidator
{
    public static bool TryParseTimecode(string? value, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split(':');
        if (parts.Length != 3 ||
            parts[0].Length < 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var remainingSeconds) ||
            minutes is < 0 or > 59 ||
            remainingSeconds is < 0 or > 59)
        {
            return false;
        }

        try
        {
            seconds = checked((hours * 60 + minutes) * 60 + remainingSeconds);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static IntervalValidationResult Validate(
        string? start,
        string? end,
        int sourceDurationSeconds,
        int maximumIntervalSeconds,
        int maximumSourceDurationSeconds)
    {
        if (!TryParseTimecode(start, out var startSeconds) ||
            !TryParseTimecode(end, out var endSeconds))
        {
            return IntervalValidationResult.Invalid("Timestamps must use HH:MM:SS.");
        }

        if (sourceDurationSeconds <= 0 || sourceDurationSeconds > maximumSourceDurationSeconds)
        {
            return IntervalValidationResult.Invalid("The source video's duration is outside the supported range.");
        }

        if (startSeconds >= endSeconds)
        {
            return IntervalValidationResult.Invalid("The end time must be later than the start time.");
        }

        if (endSeconds > sourceDurationSeconds)
        {
            return IntervalValidationResult.Invalid("The selected interval exceeds the video duration.");
        }

        if (endSeconds - startSeconds > maximumIntervalSeconds)
        {
            return IntervalValidationResult.Invalid("The selected interval exceeds the maximum allowed duration.");
        }

        return new(true, startSeconds, endSeconds, null);
    }
}
