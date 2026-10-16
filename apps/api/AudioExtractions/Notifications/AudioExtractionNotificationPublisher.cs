using Api.AudioExtractions.Contracts;

namespace Api.AudioExtractions.Notifications;

public interface IAudioExtractionNotificationPublisher
{
    Task PublishAsync(AudioExtractionStatusResponse notification, CancellationToken cancellationToken);
}

public sealed class AudioExtractionNotificationPublisher(
    AudioExtractionNotificationStreamRegistry registry) : IAudioExtractionNotificationPublisher
{
    public Task PublishAsync(
        AudioExtractionStatusResponse notification,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        registry.Publish(notification);
        return Task.CompletedTask;
    }
}
