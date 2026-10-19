namespace Api.Settings.Contracts;

// The key itself is never returned by the API, only a masked preview of it.
public sealed record YouTubeApiKeyStatus(
    bool Configured,
    string Source,
    DateTimeOffset? UpdatedAt,
    string? MaskedValue = null);

public sealed record SaveYouTubeApiKeyRequest(string? ApiKey);
