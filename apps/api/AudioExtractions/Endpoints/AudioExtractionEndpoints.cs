using System.Data.Common;
using Api.AudioExtractions.Contracts;
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

        return endpoints;
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
            ? Problem(
                StatusCodes.Status404NotFound,
                "Audio extraction not found",
                "A solicitação não foi encontrada.",
                "AUDIO_EXTRACTION_NOT_FOUND")
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
}
