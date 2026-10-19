using Api.AudioExtractions.Configuration;
using System.Text.Json;
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
    public const string YouTubeAccountName = "youtube-account";

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

    public async Task<YouTubeAccountStatus> GetYouTubeAccountStatusAsync(CancellationToken cancellationToken)
    {
        var credentials = await GetYouTubeAccountAsync(cancellationToken);
        if (credentials is null)
        {
            return new YouTubeAccountStatus(false, null, null);
        }

        var updatedAt = await dbContext.Settings
            .AsNoTracking()
            .Where(setting => setting.Key == YouTubeAccountName)
            .Select(setting => (DateTimeOffset?)setting.UpdatedAt)
            .SingleOrDefaultAsync(cancellationToken);
        return new YouTubeAccountStatus(true, credentials.Username, updatedAt);
    }

    public async Task SaveYouTubeAccountAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var protectedValue = protector.Protect(
            YouTubeAccountName,
            JsonSerializer.Serialize(new YouTubeAccountCredentials(username, password)));
        var setting = await dbContext.Settings
            .SingleOrDefaultAsync(item => item.Key == YouTubeAccountName, cancellationToken);
        if (setting is null)
        {
            dbContext.Settings.Add(new AppSettingEntity
            {
                Key = YouTubeAccountName,
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

    public async Task<bool> RemoveYouTubeAccountAsync(CancellationToken cancellationToken)
    {
        var removed = await dbContext.Settings
            .Where(setting => setting.Key == YouTubeAccountName)
            .ExecuteDeleteAsync(cancellationToken);
        return removed > 0;
    }

    public async Task<YouTubeAccountCredentials?> GetYouTubeAccountAsync(CancellationToken cancellationToken)
    {
        var protectedValue = await dbContext.Settings
            .AsNoTracking()
            .Where(setting => setting.Key == YouTubeAccountName)
            .Select(setting => setting.ProtectedValue)
            .SingleOrDefaultAsync(cancellationToken);
        if (protectedValue is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<YouTubeAccountCredentials>(
                protector.Unprotect(YouTubeAccountName, protectedValue));
        }
        catch (Exception exception) when (exception is SecretProtectionException or JsonException)
        {
            logger.LogError(exception, "The stored YouTube account could not be read.");
            return null;
        }
    }
}
