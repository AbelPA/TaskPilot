namespace Api.AudioExtractions.Contracts;

public sealed record AudioExtractionFailedData(
    string Code,
    string Message,
    bool Retryable);

public sealed record AudioExtractionFailed(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    string RequestId,
    DateTimeOffset OccurredAt,
    AudioExtractionFailedData Data);
