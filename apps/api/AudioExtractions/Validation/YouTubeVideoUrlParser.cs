namespace Api.AudioExtractions.Validation;

public static class YouTubeVideoUrlParser
{
    private static readonly HashSet<string> AllowedQueryParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "v",
        "t",
        "start",
        "feature",
        "si"
    };

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "youtu.be",
        "www.youtu.be",
        "youtube-nocookie.com",
        "www.youtube-nocookie.com"
    };

    public static bool TryParse(string? value, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 2_048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !AllowedHosts.Contains(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.IsDefaultPort ||
            !HasOnlyAllowedQueryParameters(uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? candidate = null;

        if (uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase) && segments.Length == 1)
        {
            candidate = segments[0];
        }
        else if (segments.Length == 2 && segments[0] is "embed" or "shorts" or "live")
        {
            candidate = segments[1];
        }
        else if (uri.AbsolutePath == "/watch")
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            candidate = query.TryGetValue("v", out var ids) && ids.Count == 1 ? ids[0] : null;
        }

        if (candidate is null || candidate.Length != 11 ||
            candidate.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
        {
            return false;
        }

        videoId = candidate;
        return true;
    }

    private static bool HasOnlyAllowedQueryParameters(Uri uri)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        return query.All(parameter =>
            AllowedQueryParameters.Contains(parameter.Key) &&
            parameter.Value.Count == 1);
    }
}
