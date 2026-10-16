using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Settings.Persistence;

public sealed class SettingsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SettingsDbContext>
{
    public SettingsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AudioExtraction");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__AudioExtraction to generate or apply settings migrations.");
        }

        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__SettingsMigrationsHistory"))
            .Options;
        return new SettingsDbContext(options);
    }
}
