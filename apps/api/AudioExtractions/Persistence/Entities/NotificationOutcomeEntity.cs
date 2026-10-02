namespace Api.AudioExtractions.Persistence.Entities;

public sealed class NotificationOutcomeEntity
{
    public required string RequestId { get; set; }
    public Guid EventId { get; set; }
    public required string Status { get; set; }
    public required string SafeMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public AudioExtractionRequestEntity? Request { get; set; }
}
