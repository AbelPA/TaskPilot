using Api.AudioExtractions.YouTube;

namespace Api.Settings.Services;

public sealed class YouTubeApiKeyProvider(IServiceScopeFactory scopeFactory) : IYouTubeApiKeyProvider
{
    public async Task<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ApplicationSettingsService>();
        return await settings.GetYouTubeApiKeyAsync(cancellationToken);
    }
}
