using Api.AudioExtractions.Configuration;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Microsoft.Extensions.Options;

namespace Api.AudioExtractions.Services;

public interface IAudioObjectStore
{
    Task<string> CreatePresignedGetUrlAsync(
        string objectKey,
        int expirationSeconds,
        bool asDownload,
        CancellationToken cancellationToken);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed class AudioObjectStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class MinioAudioObjectStore : IAudioObjectStore
{
    private readonly IMinioClient _signingClient;
    private readonly IMinioClient _managementClient;
    private readonly string _bucket;

    public MinioAudioObjectStore(IOptions<AudioExtractionOptions> options)
    {
        var settings = options.Value;
        var signingEndpoint = new Uri(settings.StorageEndpoint, UriKind.Absolute);
        var managementEndpoint = new Uri(
            string.IsNullOrWhiteSpace(settings.StorageManagementEndpoint)
                ? settings.StorageEndpoint
                : settings.StorageManagementEndpoint,
            UriKind.Absolute);
        _bucket = options.Value.StorageBucket;
        _signingClient = CreateClient(signingEndpoint, settings);
        _managementClient = CreateClient(managementEndpoint, settings);
    }

    public async Task<string> CreatePresignedGetUrlAsync(
        string objectKey,
        int expirationSeconds,
        bool asDownload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var arguments = new PresignedGetObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey)
                .WithExpiry(expirationSeconds);
            if (asDownload)
            {
                arguments.WithHeaders(new Dictionary<string, string>
                {
                    ["response-content-disposition"] = "attachment; filename=\"audio.mp3\"",
                });
            }

            return await _signingClient.PresignedGetObjectAsync(arguments);
        }
        catch (MinioException exception)
        {
            throw new AudioObjectStoreException(
                "The protected audio reference could not be created.",
                exception);
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await _managementClient.RemoveObjectAsync(
                new RemoveObjectArgs()
                    .WithBucket(_bucket)
                    .WithObject(objectKey),
                cancellationToken);
        }
        catch (MinioException exception)
        {
            throw new AudioObjectStoreException(
                "The expired audio artifact could not be deleted.",
                exception);
        }
    }

    private static IMinioClient CreateClient(Uri endpoint, AudioExtractionOptions settings) =>
        new MinioClient()
            .WithEndpoint(endpoint.Authority)
            .WithCredentials(settings.StorageAccessKey, settings.StorageSecretKey)
            .WithSSL(endpoint.Scheme == Uri.UriSchemeHttps)
            .Build();
}
