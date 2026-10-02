using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class AudioExtractionOpenApiTests
{
    [Fact]
    public void Create_contract_defines_required_fields_durable_acceptance_and_errors()
    {
        var contract = File.ReadAllText(FindRepositoryFile(
            "specs",
            "002-youtube-audio-extraction",
            "contracts",
            "audio-extractions.openapi.yaml"));
        var postContract = contract[
            contract.IndexOf("  /api/audio-extractions:", StringComparison.Ordinal)..];
        postContract = postContract[..postContract.IndexOf("  /api/audio-extractions/{requestId}:", StringComparison.Ordinal)];

        Assert.Contains("operationId: createAudioExtraction", postContract);
        Assert.Contains("name: Idempotency-Key", postContract);
        Assert.Contains("required: [url, start, end]", contract);
        Assert.Contains("'202':", postContract);
        Assert.Contains("durably accepted", postContract);
        Assert.Contains("'400':", postContract);
        Assert.Contains("'404':", postContract);
        Assert.Contains("'409':", postContract);
        Assert.Contains("'429':", postContract);
        Assert.Contains("'503':", postContract);
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var path = Path.Combine([current.FullName, .. pathParts]);
            if (File.Exists(path))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find the repository file {Path.Combine(pathParts)}.");
    }
}
