namespace Api.AudioExtractions.Contracts;

public sealed record AudioExtractionRequestedData(
    string VideoId,
    int StartSeconds,
    int EndSeconds,
    string OutputFormat);

public sealed record AudioExtractionRequested(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    string RequestId,
    DateTimeOffset OccurredAt,
    AudioExtractionRequestedData Data);
