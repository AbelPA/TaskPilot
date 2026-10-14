using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Api.AudioExtractions.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Api.AudioExtractions.Notifications;

public sealed class AudioExtractionsHub(
    AudioExtractionDbContext dbContext,
    TimeProvider timeProvider) : Hub
{
    private static readonly Regex RequestIdPattern = new(
        "^[A-Za-z0-9_-]{43}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task Subscribe(string requestId, CancellationToken cancellationToken)
    {
        if (!RequestIdPattern.IsMatch(requestId))
        {
            throw new HubException("A solicitação não foi encontrada.");
        }

        var now = timeProvider.GetUtcNow();
        var request = await dbContext.Requests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.RequestId == requestId, cancellationToken);
        if (request is null || (request.ExpiresAt is not null && request.ExpiresAt <= now))
        {
            throw new HubException("A solicitação não foi encontrada.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(requestId), cancellationToken);
    }

    public Task Unsubscribe(string requestId, CancellationToken cancellationToken)
    {
        if (!RequestIdPattern.IsMatch(requestId))
        {
            throw new HubException("A solicitação não foi encontrada.");
        }

        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(requestId), cancellationToken);
    }

    public static string GetGroupName(string requestId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(requestId));
        return $"audio-extraction:{Convert.ToHexString(digest)}";
    }
}
