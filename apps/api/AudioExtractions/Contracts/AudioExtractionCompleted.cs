namespace Api.AudioExtractions.Contracts;

public sealed record AudioExtractionCompletedData(
    string ObjectKey,
    string ContentType,
    int DurationSeconds,
    long SizeBytes,
    string ChecksumSha256);

public sealed record AudioExtractionCompleted(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    string RequestId,
    DateTimeOffset OccurredAt,
    AudioExtractionCompletedData Data);
