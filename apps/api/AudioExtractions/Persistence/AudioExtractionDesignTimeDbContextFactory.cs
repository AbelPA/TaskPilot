using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.AudioExtractions.Persistence;

public sealed class AudioExtractionDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<AudioExtractionDbContext>
{
    public AudioExtractionDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AudioExtraction");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__AudioExtraction to generate or apply database migrations.");
        }

        var options = new DbContextOptionsBuilder<AudioExtractionDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AudioExtractionDbContext(options);
    }
}
