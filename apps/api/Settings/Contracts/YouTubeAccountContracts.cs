namespace Api.Settings.Contracts;

// The password is never returned by the public API.
public sealed record YouTubeAccountStatus(bool Configured, string? Username, DateTimeOffset? UpdatedAt);

public sealed record SaveYouTubeAccountRequest(string? Username, string? Password);

// Only served to the worker through the token-protected internal endpoint.
public sealed record YouTubeAccountCredentials(string Username, string Password);
