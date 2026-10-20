using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Grammar.Endpoints;
using Api.Grammar.Persistence;
using Api.Grammar.Persistence.Entities;
using Api.Grammar.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AudioApi.Tests.Grammar;

public sealed class GrammarReviewSummaryEndpointTests
{
    [Fact]
    public async Task Summary_endpoint_returns_prior_comparison_when_five_prior_topic_answers_exist()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<GrammarDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddScoped<GrammarCatalogService>();
        builder.Services.AddScoped<GrammarExerciseService>();
        builder.Services.AddScoped<GrammarSubmissionHistoryService>();
        builder.Services.AddScoped<GrammarStatisticsService>();
        builder.Services.AddScoped<GrammarReviewService>();
        builder.Services.AddScoped<GrammarReviewSummaryService>();
        var app = builder.Build();
        await using (app)
        {
            Guid sessionId;
            Guid exerciseId;
            Guid topicId;
            DateTimeOffset createdAt;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<GrammarDbContext>();
                await db.Database.EnsureCreatedAsync();
                topicId = await db.Topics.Select(x => x.Id).SingleAsync();
                exerciseId = await db.Exercises.OrderBy(x => x.DisplayOrder).Select(x => x.Id).FirstAsync();
                createdAt = DateTimeOffset.UtcNow;
                sessionId = Guid.NewGuid();
                var session = new GrammarReviewSessionEntity
                {
                    Id = sessionId, CreatedAt = createdAt, SelectionSeed = 42,
                    RequestedExerciseCount = 10, AvailableExerciseCount = 1
                };
                session.Exercises.Add(new GrammarReviewExerciseEntity
                {
                    Id = Guid.NewGuid(), SessionId = sessionId, ExerciseId = exerciseId, TopicId = topicId,
                    SelectionCategory = "maintenance", Reason = "Seeded endpoint test", DisplayOrder = 0,
                    TitleSnapshot = "Exercise", TopicTitleSnapshot = "Topic"
                });
                db.ReviewSessions.Add(session);
                for (var i = 0; i < 5; i++)
                {
                    var submissionId = Guid.NewGuid();
                    var correct = i < 3;
                    db.Submissions.Add(new GrammarSubmissionEntity
                    {
                        Id = submissionId, ExerciseId = exerciseId, ExerciseTitleSnapshot = "Prior exercise",
                        TopicId = topicId, TopicTitleSnapshot = "Topic", AttemptNumber = i + 1,
                        EvaluatedAt = createdAt.AddDays(-6 + i), Score = correct ? 1 : 0, MaxScore = 1,
                        Percentage = correct ? 100 : 0, ExerciseExplanation = "Prior result",
                        PayloadHash = Guid.NewGuid().ToString("N").PadLeft(64, (char)48)
                    });
                    db.EvaluatedAnswers.Add(new EvaluatedAnswerEntity
                    {
                        Id = Guid.NewGuid(), SubmissionId = submissionId, ItemType = "blank", ItemKey = "prior",
                        SubmittedAnswer = "answer", CorrectAnswer = "answer", IsCorrect = correct,
                        Explanation = "Prior result", Points = correct ? 1 : 0, MaxPoints = 1
                    });
                }
                await db.SaveChangesAsync();
            }
            app.MapGrammarEndpoints();
            await app.StartAsync();
            using var client = app.GetTestClient();

            var submission = await client.PostAsJsonAsync($"/api/grammar/exercises/{exerciseId}/submit", new
            {
                submissionId = Guid.NewGuid(), topicId, reviewSessionId = sessionId,
                blankAnswers = new[] { new { blankKey = "1", answer = "are" }, new { blankKey = "2", answer = "am" } },
                questionAnswers = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.OK, submission.StatusCode);
            var response = await client.GetAsync($"/api/grammar/review/sessions/{sessionId}/summary");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summary = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(summary.GetProperty("priorComparisonAvailable").GetBoolean());
            Assert.Equal(60m, summary.GetProperty("priorAccuracyPercentage").GetDecimal());
            Assert.Equal(1, summary.GetProperty("exercisesAnswered").GetInt32());
        }
    }
}
