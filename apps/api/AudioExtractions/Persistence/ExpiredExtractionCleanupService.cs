using System.Data.Common;
using Api.AudioExtractions.Services;
using Microsoft.EntityFrameworkCore;

namespace Api.AudioExtractions.Persistence;

public sealed class ExpiredExtractionCleanupService(
    IServiceScopeFactory scopeFactory,
    IAudioObjectStore objectStore,
    TimeProvider timeProvider,
    ILogger<ExpiredExtractionCleanupService> logger) : BackgroundService
{
    private const int BatchSize = 100;

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        List<string> expiredRequestIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
            var terminalRequestExpiries = await dbContext.Requests.AsNoTracking()
                .Where(request => request.ExpiresAt != null)
                .Select(request => new { request.RequestId, request.ExpiresAt })
                .ToListAsync(cancellationToken);
            expiredRequestIds = terminalRequestExpiries
                .Where(request => request.ExpiresAt <= now)
                .OrderBy(request => request.ExpiresAt)
                .Select(request => request.RequestId)
                .Take(BatchSize)
                .ToList();
        }

        foreach (var requestId in expiredRequestIds)
        {
            await CleanupRequestAsync(requestId, now, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredAsync(stoppingToken);
            }
            catch (DbUpdateException exception)
            {
                logger.LogError(
                    "Expired audio cleanup could not update the database ({FailureType}); it will retry.",
                    exception.GetType().Name);
            }
            catch (DbException exception)
            {
                logger.LogError(
                    "Expired audio cleanup could not access the database ({FailureType}); it will retry.",
                    exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task CleanupRequestAsync(
        string requestId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AudioExtractionDbContext>();
        var request = await dbContext.Requests.SingleOrDefaultAsync(
            item => item.RequestId == requestId,
            cancellationToken);
        if (request?.ExpiresAt is not { } expiresAt || expiresAt > now)
        {
            return;
        }

        var expectedObjectKey = $"audio-extractions/{requestId}/audio.mp3";
        if (request.ResultObjectKey is { } objectKey)
        {
            if (request.Status != "completed" || objectKey != expectedObjectKey)
            {
                logger.LogError(
                    "Expired audio cleanup skipped an artifact with an invalid stored reference ({FailureType}).",
                    nameof(InvalidOperationException));
                return;
            }

            try
            {
                await objectStore.DeleteAsync(objectKey, cancellationToken);
            }
            catch (AudioObjectStoreException exception)
            {
                logger.LogWarning(
                    "Expired audio artifact cleanup failed ({FailureType}); its database record was retained.",
                    exception.GetType().Name);
                return;
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var outcome = await dbContext.NotificationOutcomes
            .SingleOrDefaultAsync(item => item.RequestId == requestId, cancellationToken);
        if (outcome is not null)
        {
            dbContext.NotificationOutcomes.Remove(outcome);
        }

        var outboxMessages = await dbContext.OutboxMessages
            .Where(item => item.RequestId == requestId)
            .ToListAsync(cancellationToken);
        dbContext.OutboxMessages.RemoveRange(outboxMessages);
        dbContext.Requests.Remove(request);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(
                "Expired audio database cleanup failed ({FailureType}); the record will be retried.",
                exception.GetType().Name);
        }
    }
}
