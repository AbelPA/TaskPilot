namespace Api.AudioExtractions.Persistence.Entities;

public sealed class ProcessedEventEntity
{
    public Guid EventId { get; set; }
    public required string EventType { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
