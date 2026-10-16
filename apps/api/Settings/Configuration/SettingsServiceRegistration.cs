using Api.AudioExtractions.YouTube;
using Api.Settings.Persistence;
using Api.Settings.Security;
using Api.Settings.Services;
using Microsoft.EntityFrameworkCore;

namespace Api.Settings.Configuration;

public static class SettingsServiceRegistration
{
    public static IServiceCollection AddSettings(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AudioExtraction");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:AudioExtraction must be configured before the API starts.");
        }

        services.AddOptions<SettingsOptions>()
            .Bind(configuration.GetSection(SettingsOptions.SectionName));
        services.AddDbContext<SettingsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__SettingsMigrationsHistory")));
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddScoped<ApplicationSettingsService>();
        services.AddSingleton<IYouTubeApiKeyProvider, YouTubeApiKeyProvider>();

        return services;
    }
}
