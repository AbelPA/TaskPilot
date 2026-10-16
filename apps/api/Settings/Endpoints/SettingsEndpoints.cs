using System.Text.RegularExpressions;
using Api.Settings.Contracts;
using Api.Settings.Security;
using Api.Settings.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Settings.Endpoints;

public static partial class SettingsEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9_-]{10,256}$")]
    private static partial Regex ApiKeyPattern();

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/settings").WithTags("Settings");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });

        group.MapGet("/youtube-api-key", GetYouTubeApiKeyAsync)
            .WithName("GetYouTubeApiKeyStatus")
            .Produces<YouTubeApiKeyStatus>();
        group.MapPut("/youtube-api-key", SaveYouTubeApiKeyAsync)
            .WithName("SaveYouTubeApiKey")
            .Accepts<SaveYouTubeApiKeyRequest>("application/json")
            .Produces<YouTubeApiKeyStatus>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapDelete("/youtube-api-key", RemoveYouTubeApiKeyAsync)
            .WithName("RemoveYouTubeApiKey")
            .Produces<YouTubeApiKeyStatus>();

        return endpoints;
    }

    private static async Task<IResult> GetYouTubeApiKeyAsync(
        ApplicationSettingsService settings,
        CancellationToken cancellationToken) =>
        Results.Ok(await settings.GetYouTubeApiKeyStatusAsync(cancellationToken));

    private static async Task<IResult> SaveYouTubeApiKeyAsync(
        [FromBody] SaveYouTubeApiKeyRequest request,
        ApplicationSettingsService settings,
        ILogger<ApplicationSettingsService> logger,
        CancellationToken cancellationToken)
    {
        var apiKey = request.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey) || !ApiKeyPattern().IsMatch(apiKey))
        {
            return Results.Problem(
                title: "Chave inválida",
                detail: "Informe uma chave de API do YouTube válida (10 a 256 caracteres: letras, números, '-' ou '_').",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await settings.SaveYouTubeApiKeyAsync(apiKey, cancellationToken);
        }
        catch (SecretProtectionException exception)
        {
            logger.LogError(exception, "The YouTube API key could not be encrypted.");
            return Results.Problem(
                title: "Criptografia indisponível",
                detail: "A chave de criptografia (Settings:EncryptionKey) não está configurada corretamente no servidor.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(await settings.GetYouTubeApiKeyStatusAsync(cancellationToken));
    }

    private static async Task<IResult> RemoveYouTubeApiKeyAsync(
        ApplicationSettingsService settings,
        CancellationToken cancellationToken)
    {
        await settings.RemoveYouTubeApiKeyAsync(cancellationToken);
        return Results.Ok(await settings.GetYouTubeApiKeyStatusAsync(cancellationToken));
    }
}
