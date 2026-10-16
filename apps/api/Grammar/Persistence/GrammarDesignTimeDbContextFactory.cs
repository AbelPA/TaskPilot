using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Grammar.Persistence;

public sealed class GrammarDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GrammarDbContext>
{
    public GrammarDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AudioExtraction");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__AudioExtraction to generate or apply grammar migrations.");
        }

        var options = new DbContextOptionsBuilder<GrammarDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__GrammarMigrationsHistory"))
            .Options;
        return new GrammarDbContext(options);
    }
}
