namespace Api.Settings.Contracts;

// The username and password are only returned masked by the public API.
public sealed record YouTubeAccountStatus(
    bool Configured,
    string? MaskedUsername,
    string? MaskedPassword,
    DateTimeOffset? UpdatedAt);

public sealed record SaveYouTubeAccountRequest(string? Username, string? Password);

// Only served to the worker through the token-protected internal endpoint.
public sealed record YouTubeAccountCredentials(string Username, string Password);
