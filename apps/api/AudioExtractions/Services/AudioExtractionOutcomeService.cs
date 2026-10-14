using System.Text.RegularExpressions;
using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Contracts;
using Api.AudioExtractions.Notifications;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.AudioExtractions.Services;

public sealed class AudioExtractionOutcomeService(
    AudioExtractionDbContext dbContext,
    IAudioExtractionNotificationPublisher notificationPublisher,
    IOptions<AudioExtractionOptions> options,
    TimeProvider timeProvider)
{
    private static readonly Regex RequestIdPattern = new(
        "^[A-Za-z0-9_-]{43}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ChecksumPattern = new(
        "^[a-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<AudioExtractionStatusResponse?> ApplyAsync(
        AudioExtractionCompleted message,
        CancellationToken cancellationToken)
    {
        ValidateCompleted(message);
        var request = await FindRequestAsync(message.RequestId, cancellationToken);
        if (request is null)
        {
            return null;
        }

        if (request.Status == "accepted")
        {
            var expectedDuration = request.EndSeconds - request.StartSeconds;
            if (message.Data.DurationSeconds is < 1 or > 1_800 ||
                Math.Abs(message.Data.DurationSeconds - expectedDuration) > 2 ||
                message.Data.SizeBytes > options.Value.MaximumOutputBytes ||
                !string.Equals(
                    message.Data.ObjectKey,
                    $"audio-extractions/{request.RequestId}/audio.mp3",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The completion event does not match the accepted request.");
            }
        }

        return await PersistAndNotifyAsync(
            request,
            message.EventId,
            "completed",
            null,
            null,
            message.Data,
            cancellationToken);
    }

    public async Task<AudioExtractionStatusResponse?> ApplyAsync(
        AudioExtractionFailed message,
        CancellationToken cancellationToken)
    {
        ValidateFailed(message);
        var request = await FindRequestAsync(message.RequestId, cancellationToken);
        if (request is null)
        {
            return null;
        }

        return await PersistAndNotifyAsync(
            request,
            message.EventId,
            "failed",
            message.Data.Code,
            GetSafeFailureMessage(message.Data.Code),
            null,
            cancellationToken);
    }

    public async Task<AudioExtractionStatusResponse?> GetStatusAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!RequestIdPattern.IsMatch(requestId))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var request = await dbContext.Requests.AsNoTracking()
            .Include(item => item.NotificationOutcome)
            .SingleOrDefaultAsync(
                item => item.RequestId == requestId,
                cancellationToken);
        return request is null || IsExpired(request, now) ? null : ToResponse(request);
    }

    private async Task<AudioExtractionRequestEntity?> FindRequestAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!RequestIdPattern.IsMatch(requestId))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var request = await dbContext.Requests.AsNoTracking()
            .Include(item => item.NotificationOutcome)
            .SingleOrDefaultAsync(
                item => item.RequestId == requestId,
                cancellationToken);
        return request is null || IsExpired(request, now) ? null : request;
    }

    private static bool IsExpired(AudioExtractionRequestEntity request, DateTimeOffset now) =>
        request.ExpiresAt is not null && request.ExpiresAt <= now;

    private async Task<AudioExtractionStatusResponse> PersistAndNotifyAsync(
        AudioExtractionRequestEntity request,
        Guid eventId,
        string status,
        string? failureCode,
        string? safeMessage,
        AudioExtractionCompletedData? completion,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var alreadyProcessed = await dbContext.ProcessedEvents
            .AnyAsync(item => item.EventId == eventId, cancellationToken);
        if (alreadyProcessed)
        {
            await transaction.CommitAsync(cancellationToken);
            var duplicateResponse = await GetStatusAsync(request.RequestId, cancellationToken);
            if (duplicateResponse is not null &&
                await dbContext.NotificationOutcomes.AnyAsync(
                    item => item.RequestId == request.RequestId && item.EventId == eventId,
                    cancellationToken))
            {
                await notificationPublisher.PublishAsync(duplicateResponse, cancellationToken);
            }

            return duplicateResponse
                   ?? throw new InvalidOperationException("The outcome request expired during processing.");
        }

        var now = timeProvider.GetUtcNow();
        var updated = request.Status == "accepted"
            ? await dbContext.Requests
                .Where(item => item.RequestId == request.RequestId && item.Status == "accepted")
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.Status, status)
                        .SetProperty(item => item.UpdatedAt, now)
                        .SetProperty(item => item.ExpiresAt, now.AddDays(options.Value.RetentionDays))
                        .SetProperty(item => item.ResultObjectKey, completion == null ? null : completion.ObjectKey)
                        .SetProperty(item => item.ResultSizeBytes, completion == null ? null : completion.SizeBytes)
                        .SetProperty(item => item.ResultChecksumSha256, completion == null ? null : completion.ChecksumSha256)
                        .SetProperty(item => item.ResultDurationSeconds, completion == null ? null : completion.DurationSeconds)
                        .SetProperty(item => item.FailureCode, completion == null ? failureCode : null)
                        .SetProperty(item => item.FailureMessage, completion == null ? safeMessage : null),
                    cancellationToken)
            : 0;

        if (updated > 0)
        {
            dbContext.NotificationOutcomes.Add(new NotificationOutcomeEntity
            {
                RequestId = request.RequestId,
                EventId = eventId,
                Status = status,
                SafeMessage = status == "completed" ? CompletedMessage : safeMessage!,
                CreatedAt = now,
            });
        }
        dbContext.ProcessedEvents.Add(new ProcessedEventEntity
        {
            EventId = eventId,
            EventType = $"audio.extraction.{status}",
            ProcessedAt = now,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = await GetStatusAsync(request.RequestId, cancellationToken)
                       ?? throw new InvalidOperationException("The outcome request expired during processing.");
        if (updated > 0)
        {
            await notificationPublisher.PublishAsync(response, cancellationToken);
        }

        return response;
    }

    private static AudioExtractionStatusResponse ToResponse(AudioExtractionRequestEntity request)
    {
        var outcome = request.NotificationOutcome;
        var message = request.Status switch
        {
            "accepted" => "Sua solicitação foi recebida e será processada.",
            "completed" => CompletedMessage,
            "failed" => outcome?.SafeMessage ?? "Não foi possível concluir a extração solicitada.",
            _ => throw new InvalidOperationException("Unknown audio extraction status."),
        };
        var result = request.Status == "completed"
            ? new AudioExtractionResultResponse(
                $"/api/audio-extractions/{request.RequestId}/audio",
                request.ResultDurationSeconds!.Value,
                "audio/mpeg")
            : null;
        return new AudioExtractionStatusResponse(
            request.RequestId,
            request.Status,
            message,
            outcome?.CreatedAt ?? request.CreatedAt,
            result);
    }

    private static void ValidateCompleted(AudioExtractionCompleted message)
    {
        if (message.EventId == Guid.Empty ||
            message.EventType != "audio.extraction.completed" ||
            message.SchemaVersion != 1 ||
        message.RequestId is null ||
        !RequestIdPattern.IsMatch(message.RequestId) ||
        message.OccurredAt == default ||
        message.Data is null ||
        message.Data.ContentType != "audio/mpeg" ||
        message.Data.DurationSeconds <= 0 ||
        message.Data.SizeBytes <= 0 ||
        string.IsNullOrWhiteSpace(message.Data.ObjectKey) ||
        message.Data.ObjectKey.Length is < 1 or > 512 ||
        message.Data.ChecksumSha256 is null ||
        !ChecksumPattern.IsMatch(message.Data.ChecksumSha256))
        {
            throw new InvalidDataException("The completion event does not match its contract.");
        }
    }

    private static void ValidateFailed(AudioExtractionFailed message)
    {
        if (message.EventId == Guid.Empty ||
            message.EventType != "audio.extraction.failed" ||
            message.SchemaVersion != 1 ||
            message.RequestId is null ||
            !RequestIdPattern.IsMatch(message.RequestId) ||
            message.OccurredAt == default ||
            message.Data is null ||
            message.Data.Code is not (
                "VIDEO_UNAVAILABLE" or
                "DOWNLOAD_FAILED" or
                "EXTRACTION_FAILED" or
                "STORAGE_FAILED" or
                "RETRIES_EXHAUSTED") ||
            message.Data.Retryable ||
            string.IsNullOrWhiteSpace(message.Data.Message) ||
            message.Data.Message.Length > 240)
        {
            throw new InvalidDataException("The failure event does not match its terminal contract.");
        }
    }

    private static string GetSafeFailureMessage(string code) => code switch
    {
        "VIDEO_UNAVAILABLE" => "O vídeo não está disponível para processamento.",
        "DOWNLOAD_FAILED" => "Não foi possível obter o áudio do vídeo.",
        "EXTRACTION_FAILED" => "Não foi possível extrair o intervalo solicitado.",
        "STORAGE_FAILED" => "Não foi possível armazenar o áudio extraído.",
        "RETRIES_EXHAUSTED" => "A extração não foi concluída após novas tentativas.",
        _ => throw new InvalidDataException("The failure event code is not recognized."),
    };

    private const string CompletedMessage = "A extração de áudio solicitada foi concluída.";
}
