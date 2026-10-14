using System.Diagnostics;

namespace Api.AudioExtractions.Observability;

public static class TraceContext
{
    public static string? GetCurrentTraceParent()
    {
        var activity = Activity.Current;
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C)
        {
            return null;
        }

        var traceFlags = (activity.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0 ? "01" : "00";
        return $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-{traceFlags}";
    }

    public static void AddTraceHeaders(IDictionary<string, object?> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var traceParent = GetCurrentTraceParent();
        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            headers["traceparent"] = traceParent;
        }

        var traceState = Activity.Current?.TraceStateString;
        if (!string.IsNullOrWhiteSpace(traceState))
        {
            headers["tracestate"] = traceState;
        }
    }
}
