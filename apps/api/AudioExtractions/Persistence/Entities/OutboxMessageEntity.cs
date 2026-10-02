namespace Api.AudioExtractions.Persistence.Entities;

public sealed class OutboxMessageEntity
{
    public Guid EventId { get; set; }
    public required string RequestId { get; set; }
    public required string EventType { get; set; }
    public int SchemaVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
    public required string PayloadJson { get; set; }
    public string State { get; set; } = "pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public AudioExtractionRequestEntity? Request { get; set; }
}
