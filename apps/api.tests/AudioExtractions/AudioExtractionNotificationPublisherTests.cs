using Api.AudioExtractions.Contracts;
using Api.AudioExtractions.Notifications;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class AudioExtractionNotificationPublisherTests
{
    [Fact]
    public void Registry_issues_unpredictable_short_lived_tickets()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var registry = new AudioExtractionNotificationStreamRegistry(timeProvider);
        var ticket = registry.Register(["AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"]);

        Assert.Equal(43, ticket.Length);
        Assert.True(registry.TryGetRequestIds(ticket, out _));
        timeProvider.Advance(TimeSpan.FromMinutes(30));
        Assert.False(registry.TryGetRequestIds(ticket, out _));
    }

    [Fact]
    public async Task Publisher_notifies_only_subscribers_with_the_matching_request_capability()
    {
        const string firstRequestId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string secondRequestId = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var registry = new AudioExtractionNotificationStreamRegistry(TimeProvider.System);
        var firstTicket = registry.Register([firstRequestId]);
        var secondTicket = registry.Register([secondRequestId]);
        using var firstSubscription = registry.Subscribe(firstTicket);
        using var secondSubscription = registry.Subscribe(secondTicket);
        var publisher = new AudioExtractionNotificationPublisher(registry);
        var notification = new AudioExtractionStatusResponse(
            firstRequestId,
            "completed",
            "A extração de áudio solicitada foi concluída.",
            DateTimeOffset.UtcNow,
            new AudioExtractionResultResponse(
                $"/api/audio-extractions/{firstRequestId}/audio",
                30,
                "audio/mpeg"));

        await publisher.PublishAsync(notification, CancellationToken.None);

        Assert.NotNull(firstSubscription);
        Assert.NotNull(secondSubscription);
        Assert.True(firstSubscription.Reader.TryRead(out var delivered));
        Assert.Same(notification, delivered);
        Assert.False(secondSubscription.Reader.TryRead(out _));
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
