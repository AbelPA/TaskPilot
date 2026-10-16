using System.Security.Cryptography;
using System.Threading.Channels;
using Api.AudioExtractions.Contracts;
using Microsoft.AspNetCore.WebUtilities;

namespace Api.AudioExtractions.Notifications;

public sealed class AudioExtractionNotificationStreamRegistry(TimeProvider timeProvider)
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(30);
    private readonly object gate = new();
    private readonly Dictionary<string, StreamTicket> tickets = new(StringComparer.Ordinal);

    public string Register(IEnumerable<string> requestIds)
    {
        var ticket = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var entry = new StreamTicket(
            requestIds.ToHashSet(StringComparer.Ordinal),
            timeProvider.GetUtcNow().Add(TicketLifetime));
        lock (gate)
        {
            tickets.Add(ticket, entry);
        }
        return ticket;
    }

    public bool TryGetRequestIds(string ticket, out IReadOnlyCollection<string> requestIds)
    {
        lock (gate)
        {
            RemoveExpiredTickets();
            if (tickets.TryGetValue(ticket, out var entry))
            {
                requestIds = entry.RequestIds.ToArray();
                return true;
            }
        }

        requestIds = Array.Empty<string>();
        return false;
    }

    public AudioExtractionNotificationStreamSubscription? Subscribe(string ticket)
    {
        lock (gate)
        {
            RemoveExpiredTickets();
            if (!tickets.TryGetValue(ticket, out var entry))
            {
                return null;
            }

            var subscriptionId = Guid.NewGuid();
            var channel = Channel.CreateUnbounded<AudioExtractionStatusResponse>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            entry.Subscribers.Add(subscriptionId, channel);
            return new AudioExtractionNotificationStreamSubscription(
                channel.Reader,
                () => RemoveSubscriber(ticket, subscriptionId));
        }
    }

    public void Remove(string ticket)
    {
        lock (gate)
        {
            if (tickets.Remove(ticket, out var entry))
            {
                foreach (var channel in entry.Subscribers.Values)
                {
                    channel.Writer.TryComplete();
                }
            }
        }
    }

    public void Publish(AudioExtractionStatusResponse notification)
    {
        lock (gate)
        {
            RemoveExpiredTickets();
            foreach (var ticket in tickets.Values)
            {
                if (!ticket.RequestIds.Contains(notification.RequestId))
                {
                    continue;
                }

                foreach (var channel in ticket.Subscribers.Values)
                {
                    channel.Writer.TryWrite(notification);
                }
            }
        }
    }

    private void RemoveSubscriber(string ticket, Guid subscriptionId)
    {
        lock (gate)
        {
            if (tickets.TryGetValue(ticket, out var entry) &&
                entry.Subscribers.Remove(subscriptionId, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }

    private void RemoveExpiredTickets()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var ticket in tickets
                     .Where(pair => pair.Value.ExpiresAt <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            if (tickets.Remove(ticket, out var entry))
            {
                foreach (var channel in entry.Subscribers.Values)
                {
                    channel.Writer.TryComplete();
                }
            }
        }
    }

    private sealed class StreamTicket(
        HashSet<string> requestIds,
        DateTimeOffset expiresAt)
    {
        public HashSet<string> RequestIds { get; } = requestIds;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public Dictionary<Guid, Channel<AudioExtractionStatusResponse>> Subscribers { get; } = [];
    }
}

public sealed class AudioExtractionNotificationStreamSubscription(
    ChannelReader<AudioExtractionStatusResponse> reader,
    Action dispose) : IDisposable
{
    private bool isDisposed;

    public ChannelReader<AudioExtractionStatusResponse> Reader { get; } = reader;

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        dispose();
    }
}
