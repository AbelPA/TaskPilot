namespace Api.Settings.Configuration;

public sealed class SettingsOptions
{
    public const string SectionName = "Settings";

    // Base64-encoded 256-bit key that protects secrets stored in the database.
    public string EncryptionKey { get; set; } = string.Empty;
    // Shared secret the worker presents to read the YouTube account credentials.
    public string InternalToken { get; set; } = string.Empty;
    public bool ApplyMigrationsOnStartup { get; set; } = true;
}
