using Api.AudioExtractions.Configuration;
using Api.Settings.Contracts;
using Api.Settings.Persistence;
using Api.Settings.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Settings.Services;

public sealed class ApplicationSettingsService(
    SettingsDbContext dbContext,
    ISecretProtector protector,
    IOptions<AudioExtractionOptions> audioOptions,
    TimeProvider timeProvider,
    ILogger<ApplicationSettingsService> logger)
{
    public const string YouTubeApiKeyName = "youtube-data-api-key";

    public async Task<YouTubeApiKeyStatus> GetYouTubeApiKeyStatusAsync(CancellationToken cancellationToken)
    {
        var updatedAt = await dbContext.Settings
            .AsNoTracking()
            .Where(setting => setting.Key == YouTubeApiKeyName)
            .Select(setting => (DateTimeOffset?)setting.UpdatedAt)
            .SingleOrDefaultAsync(cancellationToken);
        if (updatedAt is not null)
        {
            return new YouTubeApiKeyStatus(true, "database", updatedAt);
        }

        return string.IsNullOrWhiteSpace(audioOptions.Value.YouTubeApiKey)
            ? new YouTubeApiKeyStatus(false, "none", null)
            : new YouTubeApiKeyStatus(true, "environment", null);
    }

    public async Task SaveYouTubeApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        var protectedValue = protector.Protect(YouTubeApiKeyName, apiKey);
        var setting = await dbContext.Settings
            .SingleOrDefaultAsync(item => item.Key == YouTubeApiKeyName, cancellationToken);
        if (setting is null)
        {
            dbContext.Settings.Add(new AppSettingEntity
            {
                Key = YouTubeApiKeyName,
                ProtectedValue = protectedValue,
                UpdatedAt = timeProvider.GetUtcNow(),
            });
        }
        else
        {
            setting.ProtectedValue = protectedValue;
            setting.UpdatedAt = timeProvider.GetUtcNow();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveYouTubeApiKeyAsync(CancellationToken cancellationToken)
    {
        var removed = await dbContext.Settings
            .Where(setting => setting.Key == YouTubeApiKeyName)
            .ExecuteDeleteAsync(cancellationToken);
        return removed > 0;
    }

    // The database value wins; the AudioExtraction:YouTubeApiKey configuration is the fallback.
    public async Task<string?> GetYouTubeApiKeyAsync(CancellationToken cancellationToken)
    {
        var protectedValue = await dbContext.Settings
            .AsNoTracking()
            .Where(setting => setting.Key == YouTubeApiKeyName)
            .Select(setting => setting.ProtectedValue)
            .SingleOrDefaultAsync(cancellationToken);
        if (protectedValue is not null)
        {
            try
            {
                return protector.Unprotect(YouTubeApiKeyName, protectedValue);
            }
            catch (SecretProtectionException exception)
            {
                logger.LogError(exception, "The stored YouTube API key could not be read.");
            }
        }

        return string.IsNullOrWhiteSpace(audioOptions.Value.YouTubeApiKey)
            ? null
            : audioOptions.Value.YouTubeApiKey;
    }
}
