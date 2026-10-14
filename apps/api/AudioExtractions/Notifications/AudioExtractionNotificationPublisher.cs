using Api.AudioExtractions.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Api.AudioExtractions.Notifications;

public interface IAudioExtractionNotificationPublisher
{
    Task PublishAsync(AudioExtractionStatusResponse notification, CancellationToken cancellationToken);
}

public sealed class AudioExtractionNotificationPublisher(
    IHubContext<AudioExtractionsHub> hubContext) : IAudioExtractionNotificationPublisher
{
    public Task PublishAsync(
        AudioExtractionStatusResponse notification,
        CancellationToken cancellationToken) =>
        hubContext.Clients
            .Group(AudioExtractionsHub.GetGroupName(notification.RequestId))
            .SendAsync("extractionUpdated", notification, cancellationToken);
}
