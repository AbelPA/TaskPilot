using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Contracts;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Persistence.Entities;
using Api.AudioExtractions.Validation;
using Api.AudioExtractions.YouTube;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.AudioExtractions.Services;

public sealed record CreateAudioExtractionRequest(string Url, string Start, string End);

public sealed record AcceptedAudioExtraction(string RequestId, string Status, string Message);

public sealed class AudioExtractionValidationException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class AudioExtractionRequestService(
    AudioExtractionDbContext dbContext,
    IYouTubeVideoMetadataClient metadataClient,
    IOptions<AudioExtractionOptions> options,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions EventSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<AcceptedAudioExtraction> CreateAsync(
        CreateAudioExtractionRequest input,
        string? idempotencyKey,
        string idempotencyScope,
        CancellationToken cancellationToken)
    {
        if (!YouTubeVideoUrlParser.TryParse(input.Url, out var videoId))
        {
            throw new AudioExtractionValidationException(
                "INVALID_YOUTUBE_URL",
                "Informe um link válido de vídeo do YouTube.");
        }

        if (!ExtractionIntervalValidator.TryParseTimecode(input.Start, out var startSeconds) ||
            !ExtractionIntervalValidator.TryParseTimecode(input.End, out var endSeconds) ||
            startSeconds >= endSeconds ||
            endSeconds - startSeconds > options.Value.MaximumIntervalSeconds)
        {
            throw new AudioExtractionValidationException(
                "INVALID_INTERVAL",
                "Informe um intervalo HH:MM:SS válido de até 30 minutos.");
        }

        if (idempotencyKey is not null && (idempotencyKey.Length is < 16 or > 128))
        {
            throw new AudioExtractionValidationException(
                "INVALID_IDEMPOTENCY_KEY",
                "O cabeçalho Idempotency-Key deve conter entre 16 e 128 caracteres.");
        }

        var idempotencyHash = idempotencyKey is null
            ? null
            : Hash($"{idempotencyScope}\0{idempotencyKey}");
        var requestHash = Hash($"{videoId}\0{startSeconds}\0{endSeconds}\0mp3");
        if (idempotencyHash is not null)
        {
            var existingRequest = await dbContext.Requests.AsNoTracking()
                .SingleOrDefaultAsync(request => request.IdempotencyKeyHash == idempotencyHash, cancellationToken);
            if (existingRequest is not null)
            {
                return GetIdempotentResponse(existingRequest, requestHash);
            }
        }

        var metadata = await metadataClient.GetVideoMetadataAsync(videoId, cancellationToken);
        var interval = ExtractionIntervalValidator.Validate(
            input.Start,
            input.End,
            metadata.DurationSeconds,
            options.Value.MaximumIntervalSeconds,
            options.Value.MaximumSourceDurationSeconds);
        if (!interval.IsValid)
        {
            throw new AudioExtractionValidationException("INVALID_INTERVAL", interval.Error!);
        }

        var now = timeProvider.GetUtcNow();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);
        if (idempotencyHash is not null)
        {
            var existing = await dbContext.Requests.AsNoTracking()
                .SingleOrDefaultAsync(request => request.IdempotencyKeyHash == idempotencyHash, cancellationToken);
            if (existing is not null)
            {
                return GetIdempotentResponse(existing, requestHash);
            }
        }

        var requestId = CreateRequestId();
        var eventId = Guid.NewGuid();
        var request = new AudioExtractionRequestEntity
        {
            RequestId = requestId,
            VideoId = videoId,
            StartSeconds = interval.StartSeconds,
            EndSeconds = interval.EndSeconds,
            OutputFormat = "mp3",
            Status = "accepted",
            CreatedAt = now,
            UpdatedAt = now,
            SourceDurationSeconds = metadata.DurationSeconds,
            IdempotencyKeyHash = idempotencyHash,
            IdempotencyRequestHash = idempotencyHash is null ? null : requestHash,
        };
        var eventBody = new AudioExtractionRequested(
            eventId,
            "audio.extraction.requested",
            1,
            requestId,
            now,
            new AudioExtractionRequestedData(
                videoId,
                interval.StartSeconds,
                interval.EndSeconds,
                "mp3"));
        var outboxMessage = new OutboxMessageEntity
        {
            EventId = eventId,
            RequestId = requestId,
            EventType = eventBody.EventType,
            SchemaVersion = eventBody.SchemaVersion,
            OccurredAt = now,
            PayloadJson = JsonSerializer.Serialize(eventBody, EventSerializerOptions),
            State = "pending",
            AttemptCount = 0,
            NextAttemptAt = now,
            Request = request,
        };

        dbContext.Requests.Add(request);
        dbContext.OutboxMessages.Add(outboxMessage);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            idempotencyHash is not null &&
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_audio_extraction_requests_idempotency_key_hash"
            })
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            var existing = await dbContext.Requests.AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKeyHash == idempotencyHash, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return GetIdempotentResponse(existing, requestHash);
        }

        return new(requestId, "accepted", "Sua solicitação foi recebida e será processada.");
    }

    private static string CreateRequestId()
    {
        var encoded = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static AcceptedAudioExtraction GetIdempotentResponse(
        AudioExtractionRequestEntity existing,
        string requestHash)
    {
        if (existing.IdempotencyRequestHash is null ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(existing.IdempotencyRequestHash),
                Convert.FromHexString(requestHash)))
        {
            throw new AudioExtractionValidationException(
                "IDEMPOTENCY_KEY_REUSED",
                "Esta chave de idempotência já foi usada com outros dados.");
        }

        return new(existing.RequestId, "accepted", "Sua solicitação foi recebida e será processada.");
    }
}
