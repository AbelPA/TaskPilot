using System.Data.Common;
using System.Text.Json;
using System.Threading.Channels;
using Api.AudioExtractions.Contracts;
using Api.AudioExtractions.Notifications;
using Api.AudioExtractions.Services;
using Api.AudioExtractions.YouTube;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.AudioExtractions.Endpoints;

public static class AudioExtractionEndpoints
{
    public static IEndpointRouteBuilder MapAudioExtractionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/audio-extractions", CreateAsync)
            .RequireRateLimiting("audio-extraction-submissions")
            .WithName("CreateAudioExtraction")
            .Accepts<CreateAudioExtractionRequest>("application/json")
            .Produces<Api.AudioExtractions.Services.AcceptedAudioExtraction>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("Audio extractions");

        endpoints.MapGet("/api/audio-extractions/{requestId}", GetStatusAsync)
            .WithName("GetAudioExtraction")
            .Produces<AudioExtractionStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Audio extractions");

        endpoints.MapGet("/api/audio-extractions/{requestId}/audio", GetAudioResultAsync)
            .WithName("GetAudioExtractionAudio")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("Audio extractions");

        endpoints.MapPost(
                "/api/audio-extractions/notification-streams",
                RegisterNotificationStreamAsync)
            .WithName("RegisterAudioExtractionNotificationStream")
            .Accepts<RegisterAudioExtractionNotificationStreamRequest>("application/json")
            .Produces<RegisteredAudioExtractionNotificationStream>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Audio extractions");

        endpoints.MapGet(
                "/api/audio-extractions/notification-streams/{streamTicket}/events",
                OpenNotificationStreamAsync)
            .WithName("GetAudioExtractionNotificationStream")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Audio extractions");

        return endpoints;
    }

    private static async Task<IResult> RegisterNotificationStreamAsync(
        [FromBody] RegisterAudioExtractionNotificationStreamRequest? request,
        HttpContext httpContext,
        AudioExtractionOutcomeService outcomeService,
        AudioExtractionNotificationStreamRegistry registry,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (request?.RequestIds is not { Count: > 0 })
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Invalid notification stream request",
                "Informe ao menos uma solicitação válida.",
                "INVALID_NOTIFICATION_STREAM_REQUEST");
        }

        if (request.RequestIds.Any(string.IsNullOrEmpty))
        {
            return AudioExtractionNotFound();
        }

        var requestIds = request.RequestIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var requestId in requestIds)
        {
            if (await outcomeService.GetStatusAsync(requestId, cancellationToken) is null)
            {
                return AudioExtractionNotFound();
            }
        }

        var ticket = registry.Register(requestIds);
        var streamUrl = $"/api/audio-extractions/notification-streams/{ticket}/events";
        return Results.Created(streamUrl, new RegisteredAudioExtractionNotificationStream(streamUrl));
    }

    private static async Task<IResult> OpenNotificationStreamAsync(
        string streamTicket,
        HttpContext httpContext,
        AudioExtractionOutcomeService outcomeService,
        AudioExtractionNotificationStreamRegistry registry,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!registry.TryGetRequestIds(streamTicket, out var requestIds))
        {
            return AudioExtractionNotFound();
        }

        using var subscription = registry.Subscribe(streamTicket);
        if (subscription is null)
        {
            return AudioExtractionNotFound();
        }

        var initialStatuses = new List<AudioExtractionStatusResponse>(requestIds.Count);
        foreach (var requestId in requestIds)
        {
            var status = await outcomeService.GetStatusAsync(requestId, cancellationToken);
            if (status is null)
            {
                registry.Remove(streamTicket);
                return AudioExtractionNotFound();
            }

            initialStatuses.Add(status);
        }

        var pendingRequestIds = requestIds.ToHashSet(StringComparer.Ordinal);
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";
        await httpContext.Response.StartAsync(cancellationToken);
        await httpContext.Response.WriteAsync("retry: 3000\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);

        foreach (var status in initialStatuses)
        {
            if (IsTerminal(status.Status))
            {
                await WriteOutcomeAsync(httpContext, status, cancellationToken);
                pendingRequestIds.Remove(status.RequestId);
            }
        }

        var lastHeartbeat = timeProvider.GetTimestamp();
        while (pendingRequestIds.Count > 0)
        {
            var remainingUntilHeartbeat =
                TimeSpan.FromSeconds(15) - timeProvider.GetElapsedTime(lastHeartbeat);
            if (remainingUntilHeartbeat <= TimeSpan.Zero)
            {
                if (!registry.TryGetRequestIds(streamTicket, out _))
                {
                    break;
                }

                await WriteHeartbeatAsync(httpContext, cancellationToken);
                lastHeartbeat = timeProvider.GetTimestamp();
                continue;
            }

            using var heartbeatCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            heartbeatCancellation.CancelAfter(remainingUntilHeartbeat);
            try
            {
                var notification = await subscription.Reader.ReadAsync(heartbeatCancellation.Token);
                if (pendingRequestIds.Remove(notification.RequestId))
                {
                    await WriteOutcomeAsync(httpContext, notification, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!registry.TryGetRequestIds(streamTicket, out _))
                {
                    break;
                }

                await WriteHeartbeatAsync(httpContext, cancellationToken);
                lastHeartbeat = timeProvider.GetTimestamp();
            }
            catch (ChannelClosedException)
            {
                break;
            }
        }

        return Results.Empty;
    }

    private static async Task WriteHeartbeatAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await httpContext.Response.WriteAsync(
            "event: heartbeat\ndata: {}\n\n",
            cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteOutcomeAsync(
        HttpContext httpContext,
        AudioExtractionStatusResponse status,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(status, JsonSerializerOptions.Web);
        await httpContext.Response.WriteAsync(
            $"event: extractionUpdated\ndata: {data}\n\n",
            cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static bool IsTerminal(string status) => status is "completed" or "failed";

    private static async Task<IResult> GetAudioResultAsync(
        string requestId,
        HttpContext httpContext,
        AudioResultAccessService service,
        CancellationToken cancellationToken,
        [FromQuery(Name = "download")] bool download = false)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        try
        {
            var signedUrl = await service.GetProtectedAudioUrlAsync(
                requestId,
                download,
                cancellationToken);
            return signedUrl is null
                ? Problem(
                    StatusCodes.Status404NotFound,
                    "Audio extraction not found",
                    "A solicitação não foi encontrada.",
                    "AUDIO_EXTRACTION_NOT_FOUND")
                : Results.Redirect(signedUrl);
        }
        catch (AudioObjectStoreException)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Audio result unavailable",
                "O áudio não está disponível para acesso agora. Tente novamente.",
                "AUDIO_RESULT_UNAVAILABLE");
        }
    }

    private static async Task<IResult> GetStatusAsync(
        string requestId,
        HttpContext httpContext,
        AudioExtractionOutcomeService service,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        var status = await service.GetStatusAsync(requestId, cancellationToken);
        return status is null
            ? AudioExtractionNotFound()
            : Results.Ok(status);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateAudioExtractionRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        HttpContext httpContext,
        AudioExtractionRequestService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Invalid audio extraction request",
                "Informe um corpo de solicitação válido.",
                "INVALID_REQUEST");
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        try
        {
            var accepted = await service.CreateAsync(
                request,
                idempotencyKey,
                ipAddress,
                cancellationToken);
            return Results.Accepted(
                $"/api/audio-extractions/{accepted.RequestId}",
                accepted);
        }
        catch (AudioExtractionValidationException exception)
        {
            var status = exception.Code == "IDEMPOTENCY_KEY_REUSED"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            return Problem(status, "Invalid audio extraction request", exception.Message, exception.Code);
        }
        catch (YouTubeVideoNotFoundException)
        {
            return Problem(
                StatusCodes.Status404NotFound,
                "Video not found",
                "O vídeo não existe ou não está disponível para processamento.",
                "VIDEO_NOT_FOUND");
        }
        catch (YouTubeMetadataUnavailableException)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Video verification unavailable",
                "Não foi possível verificar o vídeo agora. Tente novamente.",
                "VIDEO_METADATA_UNAVAILABLE");
        }
        catch (HttpRequestException)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Video verification unavailable",
                "Não foi possível verificar o vídeo agora. Tente novamente.",
                "VIDEO_METADATA_UNAVAILABLE");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Video verification timed out",
                "A verificação do vídeo demorou demais. Tente novamente.",
                "VIDEO_METADATA_TIMEOUT");
        }
        catch (DbUpdateException)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Request storage unavailable",
                "Não foi possível registrar a solicitação. Tente novamente.",
                "REQUEST_STORAGE_UNAVAILABLE");
        }
        catch (NpgsqlException)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Request storage unavailable",
                "Não foi possível registrar a solicitação. Tente novamente.",
                "REQUEST_STORAGE_UNAVAILABLE");
        }
        catch (DbException)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Request storage unavailable",
                "Não foi possível registrar a solicitação. Tente novamente.",
                "REQUEST_STORAGE_UNAVAILABLE");
        }
    }

    private static IResult Problem(int status, string title, string detail, string code) =>
        Results.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult AudioExtractionNotFound() =>
        Problem(
            StatusCodes.Status404NotFound,
            "Audio extraction not found",
            "A solicitação não foi encontrada.",
            "AUDIO_EXTRACTION_NOT_FOUND");
}

public sealed record RegisterAudioExtractionNotificationStreamRequest(IReadOnlyList<string>? RequestIds);

public sealed record RegisteredAudioExtractionNotificationStream(string StreamUrl);
