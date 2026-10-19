using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Api.Settings.Configuration;
using Api.Settings.Contracts;
using Api.Settings.Security;
using Api.Settings.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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

        group.MapGet("/youtube-account", GetYouTubeAccountAsync)
            .WithName("GetYouTubeAccountStatus")
            .Produces<YouTubeAccountStatus>();
        group.MapPut("/youtube-account", SaveYouTubeAccountAsync)
            .WithName("SaveYouTubeAccount")
            .Accepts<SaveYouTubeAccountRequest>("application/json")
            .Produces<YouTubeAccountStatus>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapDelete("/youtube-account", RemoveYouTubeAccountAsync)
            .WithName("RemoveYouTubeAccount")
            .Produces<YouTubeAccountStatus>();

        // Not under /api, so the web proxy never exposes it; the worker reaches it on the internal network.
        endpoints.MapGet("/internal/youtube-account", GetInternalYouTubeAccountAsync)
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> GetYouTubeAccountAsync(
        ApplicationSettingsService settings,
        CancellationToken cancellationToken) =>
        Results.Ok(await settings.GetYouTubeAccountStatusAsync(cancellationToken));

    private static async Task<IResult> SaveYouTubeAccountAsync(
        [FromBody] SaveYouTubeAccountRequest request,
        ApplicationSettingsService settings,
        ILogger<ApplicationSettingsService> logger,
        CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        var password = request.Password;
        if (string.IsNullOrEmpty(username) || username.Length > 254 || username.Any(char.IsControl))
        {
            return Results.Problem(
                title: "Usuário inválido",
                detail: "Informe o usuário (e-mail) da conta do Google com até 254 caracteres.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrEmpty(password) || password.Length > 256 || password.Any(char.IsControl))
        {
            return Results.Problem(
                title: "Senha inválida",
                detail: "Informe a senha da conta com até 256 caracteres.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await settings.SaveYouTubeAccountAsync(username, password, cancellationToken);
        }
        catch (SecretProtectionException exception)
        {
            logger.LogError(exception, "The YouTube account could not be encrypted.");
            return Results.Problem(
                title: "Criptografia indisponível",
                detail: "A chave de criptografia (Settings:EncryptionKey) não está configurada corretamente no servidor.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(await settings.GetYouTubeAccountStatusAsync(cancellationToken));
    }

    private static async Task<IResult> RemoveYouTubeAccountAsync(
        ApplicationSettingsService settings,
        CancellationToken cancellationToken)
    {
        await settings.RemoveYouTubeAccountAsync(cancellationToken);
        return Results.Ok(await settings.GetYouTubeAccountStatusAsync(cancellationToken));
    }

    private static async Task<IResult> GetInternalYouTubeAccountAsync(
        HttpContext context,
        ApplicationSettingsService settings,
        IOptions<SettingsOptions> options,
        CancellationToken cancellationToken)
    {
        var expected = options.Value.InternalToken;
        var provided = context.Request.Headers["X-Internal-Token"].ToString();
        if (string.IsNullOrEmpty(expected) ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(provided))))
        {
            return Results.Unauthorized();
        }

        context.Response.Headers.CacheControl = "no-store";
        var credentials = await settings.GetYouTubeAccountAsync(cancellationToken);
        return credentials is null ? Results.NoContent() : Results.Ok(credentials);
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
