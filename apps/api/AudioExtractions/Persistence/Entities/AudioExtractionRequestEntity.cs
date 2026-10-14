namespace Api.AudioExtractions.Persistence.Entities;

public sealed class AudioExtractionRequestEntity
{
    public required string RequestId { get; set; }
    public required string VideoId { get; set; }
    public int StartSeconds { get; set; }
    public int EndSeconds { get; set; }
    public string OutputFormat { get; set; } = "mp3";
    public string Status { get; set; } = "accepted";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int SourceDurationSeconds { get; set; }
    public string? ResultObjectKey { get; set; }
    public long? ResultSizeBytes { get; set; }
    public string? ResultChecksumSha256 { get; set; }
    public int? ResultDurationSeconds { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? IdempotencyKeyHash { get; set; }
    public string? IdempotencyRequestHash { get; set; }
    public ICollection<OutboxMessageEntity> OutboxMessages { get; set; } = new List<OutboxMessageEntity>();
    public NotificationOutcomeEntity? NotificationOutcome { get; set; }
}
