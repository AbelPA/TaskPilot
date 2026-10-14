namespace Api.AudioExtractions.Configuration;

public sealed class AudioExtractionOptions
{
    public const string SectionName = "AudioExtraction";

    public string YouTubeApiKey { get; set; } = string.Empty;
    public int MaximumIntervalSeconds { get; set; } = 1_800;
    public int MaximumSourceDurationSeconds { get; set; } = 21_600;
    public int RequestsPerMinutePerIp { get; set; } = 5;
    public long MaximumDownloadBytes { get; set; } = 2_147_483_648;
    public int MaximumOutputBytes { get; set; } = 104_857_600;
    public int WorkerTimeoutMinutes { get; set; } = 15;
    public int RetentionDays { get; set; } = 7;
    public string StorageEndpoint { get; set; } = string.Empty;
    public string StorageManagementEndpoint { get; set; } = string.Empty;
    public string StorageAccessKey { get; set; } = string.Empty;
    public string StorageSecretKey { get; set; } = string.Empty;
    public string StorageBucket { get; set; } = string.Empty;
    public int SignedUrlTtlSeconds { get; set; } = 300;
}
