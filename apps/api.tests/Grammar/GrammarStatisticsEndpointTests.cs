using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Grammar.Endpoints;
using Api.Grammar.Persistence;
using Api.Grammar.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AudioApi.Tests.Grammar;

public sealed class GrammarStatisticsEndpointTests
{
    [Fact]
    public async Task Get_topic_statistics_returns_empty_history_state_and_contract_fields()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<GrammarDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddScoped<GrammarStatisticsService>();
        builder.Services.AddScoped<GrammarCatalogService>();
        builder.Services.AddScoped<GrammarExerciseService>();
        builder.Services.AddScoped<GrammarSubmissionHistoryService>();
        builder.Services.AddScoped<GrammarReviewService>();
        builder.Services.AddScoped<GrammarReviewSummaryService>();
        var app = builder.Build();
        await using (app)
        {
            await using (var scope = app.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<GrammarDbContext>().Database.EnsureCreatedAsync();
            app.MapGrammarEndpoints();
            await app.StartAsync();
            using var client = app.GetTestClient();

            var response = await client.GetAsync("/api/grammar/statistics/topics");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement[]>();
            Assert.NotNull(body);
            var topic = Assert.Single(body);
            Assert.True(topic.TryGetProperty("topicId", out var topicId));
            Assert.Equal("b9a9e289-6e16-4f17-a144-81b12f2c5e01", topicId.GetGuid().ToString());
            Assert.Equal("present-simple-to-be", topic.GetProperty("topicSlug").GetString());
            Assert.Equal("Present Simple: verb to be", topic.GetProperty("topicTitle").GetString());
            Assert.Equal(0, topic.GetProperty("totalAttempts").GetInt32());
            Assert.Equal(0, topic.GetProperty("correctAnswers").GetInt32());
            Assert.Equal(0, topic.GetProperty("incorrectAnswers").GetInt32());
            Assert.Equal(JsonValueKind.Null, topic.GetProperty("accuracyPercentage").ValueKind);
            Assert.Equal(JsonValueKind.Null, topic.GetProperty("errorPercentage").ValueKind);
            Assert.Equal(JsonValueKind.Null, topic.GetProperty("lastAttemptAt").ValueKind);
            Assert.Equal(0, topic.GetProperty("recentAttemptCount").GetInt32());
            Assert.Equal(0, topic.GetProperty("recentCorrectAnswers").GetInt32());
            Assert.Equal(0, topic.GetProperty("recentIncorrectAnswers").GetInt32());
            Assert.False(topic.GetProperty("assessed").GetBoolean());
            Assert.True(topic.GetProperty("insufficientEvidence").GetBoolean());
        }
    }
}
