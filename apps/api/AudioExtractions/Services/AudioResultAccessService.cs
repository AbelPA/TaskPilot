using System.Text.RegularExpressions;
using Api.AudioExtractions.Configuration;
using Api.AudioExtractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.AudioExtractions.Services;

public sealed partial class AudioResultAccessService(
    AudioExtractionDbContext dbContext,
    IAudioObjectStore objectStore,
    IOptions<AudioExtractionOptions> options,
    TimeProvider timeProvider)
{
    public async Task<string?> GetProtectedAudioUrlAsync(
        string requestId,
        bool asDownload,
        CancellationToken cancellationToken)
    {
        if (!RequestIdPattern().IsMatch(requestId))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var request = await dbContext.Requests.AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.RequestId == requestId &&
                    item.Status == "completed" &&
                    item.ResultObjectKey != null,
                cancellationToken);
        if (request?.ResultObjectKey is not { } objectKey ||
            request.ExpiresAt is not { } expiresAt ||
            expiresAt <= now)
        {
            return null;
        }

        var remainingTtlSeconds = (int)Math.Floor((expiresAt - now).TotalSeconds);
        if (remainingTtlSeconds < 1)
        {
            return null;
        }

        return await objectStore.CreatePresignedGetUrlAsync(
            objectKey,
            Math.Min(options.Value.SignedUrlTtlSeconds, remainingTtlSeconds),
            asDownload,
            cancellationToken);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex RequestIdPattern();
}
