namespace Api.AudioExtractions.Contracts;

public sealed record AudioExtractionResultResponse(
    string AudioPath,
    int DurationSeconds,
    string ContentType);

public sealed record AudioExtractionStatusResponse(
    string RequestId,
    string Status,
    string Message,
    DateTimeOffset CreatedAt,
    AudioExtractionResultResponse? Result);
