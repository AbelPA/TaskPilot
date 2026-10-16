namespace Api.Settings.Contracts;

// The key itself is never returned by the API.
public sealed record YouTubeApiKeyStatus(bool Configured, string Source, DateTimeOffset? UpdatedAt);

public sealed record SaveYouTubeApiKeyRequest(string? ApiKey);
