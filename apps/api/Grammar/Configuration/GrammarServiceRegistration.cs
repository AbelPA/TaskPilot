using Api.Grammar.Persistence;
using Api.Grammar.Services;
using Microsoft.EntityFrameworkCore;

namespace Api.Grammar.Configuration;

public static class GrammarServiceRegistration
{
    public static IServiceCollection AddGrammar(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AudioExtraction");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:AudioExtraction must be configured before the API starts.");
        }

        services.AddDbContext<GrammarDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__GrammarMigrationsHistory")));
        services.AddScoped<GrammarCatalogService>();
        services.AddScoped<GrammarExerciseService>();
        services.AddScoped<GrammarSubmissionHistoryService>();
        services.AddScoped<GrammarStatisticsService>();
        services.AddScoped<GrammarReviewService>();
        services.AddScoped<GrammarReviewSummaryService>();

        return services;
    }
}
