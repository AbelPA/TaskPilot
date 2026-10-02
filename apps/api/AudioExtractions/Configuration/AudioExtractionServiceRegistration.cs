using System.Threading.RateLimiting;
using Api.AudioExtractions.Messaging;
using Api.AudioExtractions.Notifications;
using Api.AudioExtractions.Persistence;
using Api.AudioExtractions.Services;
using Api.AudioExtractions.YouTube;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.AudioExtractions.Configuration;

public static class AudioExtractionServiceRegistration
{
    public static IServiceCollection AddAudioExtractions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AudioExtractionOptions>()
            .Bind(configuration.GetSection(AudioExtractionOptions.SectionName))
            .Validate(options => options.MaximumIntervalSeconds is > 0 and <= 1_800)
            .Validate(options => options.MaximumSourceDurationSeconds is > 0 and <= 21_600)
            .Validate(options => options.RequestsPerMinutePerIp > 0)
            .Validate(options => options.MaximumDownloadBytes > 0)
            .Validate(options => options.MaximumOutputBytes > 0)
            .Validate(options => options.WorkerTimeoutMinutes > 0)
            .Validate(options => options.RetentionDays > 0)
            .Validate(options => !string.IsNullOrWhiteSpace(options.YouTubeApiKey),
                "AudioExtraction:YouTubeApiKey must be configured.")
            .ValidateOnStart();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        var connectionString = configuration.GetConnectionString("AudioExtraction");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:AudioExtraction must be configured before the API starts.");
        }

        services.AddDbContext<AudioExtractionDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSignalR();
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.HostName))
            .Validate(options => options.Port is > 0 and <= 65_535)
            .Validate(options => !string.IsNullOrWhiteSpace(options.UserName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Password))
            .ValidateOnStart();
        services.AddHttpClient<IYouTubeVideoMetadataClient, YouTubeVideoMetadataClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            })
            .RemoveAllLoggers();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOutboxMessagePublisher, RabbitMqOutboxMessagePublisher>();
        services.AddHostedService<OutboxPublisher>();
        services.AddScoped<AudioExtractionRequestService>();
        services.AddScoped<AudioExtractionOutcomeService>();
        services.AddSingleton<IAudioExtractionNotificationPublisher, AudioExtractionNotificationPublisher>();
        services.AddHostedService<AudioExtractionOutcomeConsumer>();
        services.AddRateLimiter(rateLimiter =>
        {
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiter.AddPolicy("audio-extraction-submissions", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = context.RequestServices
                            .GetRequiredService<IOptions<AudioExtractionOptions>>()
                            .Value.RequestsPerMinutePerIp,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            rateLimiter.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        type = "about:blank",
                        title = "Rate limit exceeded",
                        status = StatusCodes.Status429TooManyRequests,
                        detail = "Muitas solicitações. Aguarde um minuto e tente novamente.",
                        code = "RATE_LIMITED",
                    });
                await context.HttpContext.Response.Body.WriteAsync(payload, cancellationToken);
            };
        });

        return services;
    }
}
