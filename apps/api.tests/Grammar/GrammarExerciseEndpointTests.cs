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

public sealed class GrammarExerciseEndpointTests
{
    [Fact]
    public async Task Exercise_get_hides_answers_and_submission_returns_per_item_correction()
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
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<GrammarDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
            }

            app.MapGrammarEndpoints();
            await app.StartAsync();
            using var client = app.GetTestClient();

            var topics = await client.GetFromJsonAsync<JsonElement[]>("/api/grammar/topics");
            Assert.NotNull(topics);
            Assert.Single(topics);
            Assert.Equal("A1", topics[0].GetProperty("level").GetString());
            Assert.Equal(2, topics[0].GetProperty("exerciseCount").GetInt32());

            var exerciseSummaries = await client.GetFromJsonAsync<JsonElement[]>(
                "/api/grammar/topics/present-simple-to-be/exercises");
            Assert.NotNull(exerciseSummaries);
            Assert.Equal(2, exerciseSummaries.Length);
            var blanksExerciseId = exerciseSummaries[0].GetProperty("id").GetGuid();

            var exerciseResponse = await client.GetAsync($"/api/grammar/exercises/{blanksExerciseId}");
            Assert.Equal(HttpStatusCode.OK, exerciseResponse.StatusCode);
            var exercise = await exerciseResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(exercise.TryGetProperty("explanation", out _));
            Assert.Contains("\n\n", exercise.GetProperty("contentTemplate").GetString());
            Assert.Equal(2, exercise.GetProperty("blanks").GetArrayLength());
            foreach (var blank in exercise.GetProperty("blanks").EnumerateArray())
            {
                Assert.False(blank.TryGetProperty("answers", out _));
                foreach (var option in blank.GetProperty("options").EnumerateArray())
                {
                    Assert.False(option.TryGetProperty("isCorrect", out _));
                    Assert.False(option.TryGetProperty("explanation", out _));
                }
            }

            var stableSubmissionId = Guid.NewGuid();
            var submissionPayload = new
                {
                    submissionId = stableSubmissionId,
                    blankAnswers = new[]
                    {
                        new { blankKey = "1", answer = " Are " },
                        new { blankKey = "2", answer = "am" },
                    },
                    questionAnswers = Array.Empty<object>(),
                };
            var submitted = await client.PostAsJsonAsync($"/api/grammar/exercises/{blanksExerciseId}/submit", submissionPayload);
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
            var result = await submitted.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2m, result.GetProperty("score").GetDecimal());
            Assert.Equal(100m, result.GetProperty("percentage").GetDecimal());
            Assert.Equal(2, result.GetProperty("results").GetArrayLength());
            Assert.True(result.GetProperty("results")[0].GetProperty("isCorrect").GetBoolean());
            Assert.True(result.TryGetProperty("explanation", out _));
            Assert.Equal(stableSubmissionId, result.GetProperty("submissionId").GetGuid());
            Assert.Equal(1, result.GetProperty("attemptNumber").GetInt32());
            var replay = await client.PostAsJsonAsync($"/api/grammar/exercises/{blanksExerciseId}/submit", submissionPayload);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(stableSubmissionId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submissionId").GetGuid());
            var conflict = await client.PostAsJsonAsync($"/api/grammar/exercises/{blanksExerciseId}/submit", new
            {
                submissionId = stableSubmissionId,
                blankAnswers = new[] { new { blankKey = "1", answer = "is" }, new { blankKey = "2", answer = "am" } },
                questionAnswers = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            var stats = await client.GetFromJsonAsync<JsonElement[]>("/api/grammar/statistics/topics");
            Assert.NotNull(stats);
            Assert.Equal(2, stats[0].GetProperty("totalAttempts").GetInt32());
            var missingSummary = await client.GetAsync($"/api/grammar/review/sessions/{Guid.NewGuid()}/summary");
            Assert.Equal(HttpStatusCode.NotFound, missingSummary.StatusCode);
            var sessionResponse = await client.PostAsync("/api/grammar/review/sessions", null);
            Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
            var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, session.GetProperty("exerciseCount").GetInt32());
            var reviewSummary = await client.GetFromJsonAsync<JsonElement>($"/api/grammar/review/sessions/{session.GetProperty("sessionId").GetGuid()}/summary");
            Assert.Equal(0, reviewSummary.GetProperty("exercisesAnswered").GetInt32());
            Assert.False(reviewSummary.GetProperty("priorComparisonAvailable").GetBoolean());
            Assert.Equal(JsonValueKind.Null, reviewSummary.GetProperty("priorAccuracyPercentage").ValueKind);
            var sessionId = session.GetProperty("sessionId").GetGuid();
            var topicId = stats[0].GetProperty("topicId").GetGuid();
            var reviewSubmission = await client.PostAsJsonAsync($"/api/grammar/exercises/{blanksExerciseId}/submit", new
            {
                submissionId = Guid.NewGuid(), topicId, reviewSessionId = sessionId,
                blankAnswers = new[] { new { blankKey = "1", answer = "are" }, new { blankKey = "2", answer = "is" } },
                questionAnswers = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.OK, reviewSubmission.StatusCode);
            var completedSummary = await client.GetFromJsonAsync<JsonElement>($"/api/grammar/review/sessions/{sessionId}/summary");
            Assert.Equal(1, completedSummary.GetProperty("exercisesAnswered").GetInt32());
            Assert.Equal(1, completedSummary.GetProperty("correctAnswers").GetInt32());
            Assert.Equal(1, completedSummary.GetProperty("topicResults").GetArrayLength());
            Assert.True(completedSummary.GetProperty("topicResults")[0].GetProperty("needsAttention").GetBoolean());



            var invalidSubmission = await client.PostAsJsonAsync(
                $"/api/grammar/exercises/{blanksExerciseId}/submit",
                new
                {
                    submissionId = Guid.NewGuid(),
                    blankAnswers = new[] { new { blankKey = "unknown", answer = "is" } },
                    questionAnswers = Array.Empty<object>(),
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidSubmission.StatusCode);

            var choiceExerciseId = exerciseSummaries[1].GetProperty("id").GetGuid();
            var choiceSubmission = await client.PostAsJsonAsync(
                $"/api/grammar/exercises/{choiceExerciseId}/submit",
                new
                {
                    submissionId = Guid.NewGuid(),
                    blankAnswers = Array.Empty<object>(),
                    questionAnswers = new[] { new { questionKey = "1", optionKey = "B" } },
                });
            Assert.Equal(HttpStatusCode.OK, choiceSubmission.StatusCode);
            var choiceResult = await choiceSubmission.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1m, choiceResult.GetProperty("score").GetDecimal());
            Assert.Equal("B) is", choiceResult.GetProperty("results")[0]
                .GetProperty("correctAnswer").GetString());

            var choiceExercise = await client.GetFromJsonAsync<JsonElement>(
                $"/api/grammar/exercises/{choiceExerciseId}");
            Assert.Equal("multiple_choice", choiceExercise.GetProperty("exerciseType").GetString());
            Assert.False(choiceExercise.TryGetProperty("explanation", out _));
            Assert.False(choiceExercise.GetProperty("questions")[0].GetProperty("options")[1]
                .TryGetProperty("isCorrect", out _));

            var incorrectSubmission = await client.PostAsJsonAsync(
                $"/api/grammar/exercises/{blanksExerciseId}/submit",
                new
                {
                    submissionId = Guid.NewGuid(),
                    blankAnswers = new[]
                    {
                        new { blankKey = "1", answer = "is" },
                        new { blankKey = "2", answer = "" },
                    },
                    questionAnswers = Array.Empty<object>(),
                });
            var incorrectResult = await incorrectSubmission.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.OK, incorrectSubmission.StatusCode);
            Assert.Equal(0m, incorrectResult.GetProperty("score").GetDecimal());
            Assert.Equal("Are", incorrectResult.GetProperty("results")[0]
                .GetProperty("correctAnswer").GetString());

            var newTopicId = Guid.NewGuid();
            var newExerciseId = Guid.NewGuid();
            var newBlankId = Guid.NewGuid();
            var newQuestionDate = DateTimeOffset.UtcNow;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<GrammarDbContext>();
                dbContext.Topics.Add(new Api.Grammar.Persistence.Entities.GrammarTopicEntity
                {
                    Id = newTopicId,
                    Slug = "past-simple-new",
                    Title = "Past Simple",
                    Description = "Ações concluídas no passado.",
                    Level = "A2",
                    DisplayOrder = 2,
                    IsActive = true,
                    CreatedAt = newQuestionDate,
                    UpdatedAt = newQuestionDate,
                });
                dbContext.TheorySections.Add(new Api.Grammar.Persistence.Entities.GrammarTheorySectionEntity
                {
                    Id = Guid.NewGuid(),
                    TopicId = newTopicId,
                    Title = "Past Simple",
                    ContentJson = """[{"type":"paragraph","text":"Use the past form of the verb."}]""",
                    DisplayOrder = 1,
                });
                dbContext.Exercises.Add(new Api.Grammar.Persistence.Entities.ExerciseEntity
                {
                    Id = newExerciseId,
                    Title = "Past simple practice",
                    Instruction = "Complete the sentence.",
                    ExerciseType = "fill_blank",
                    ContentTemplate = "Yesterday, {{blank:1}}.",
                    Explanation = "Use the past form.",
                    DisplayOrder = 1,
                    IsActive = true,
                    CreatedAt = newQuestionDate,
                    UpdatedAt = newQuestionDate,
                });
                dbContext.ExerciseTopics.Add(new Api.Grammar.Persistence.Entities.ExerciseTopicEntity
                {
                    ExerciseId = newExerciseId,
                    TopicId = newTopicId,
                });
                dbContext.Blanks.Add(new Api.Grammar.Persistence.Entities.ExerciseBlankEntity
                {
                    Id = newBlankId,
                    ExerciseId = newExerciseId,
                    BlankKey = "1",
                    DisplayOrder = 1,
                    InputType = "text",
                    Points = 1,
                });
                dbContext.BlankAnswers.Add(new Api.Grammar.Persistence.Entities.ExerciseBlankAnswerEntity
                {
                    Id = Guid.NewGuid(),
                    BlankId = newBlankId,
                    Answer = "worked",
                    IsPrimary = true,
                });
                await dbContext.SaveChangesAsync();
            }

            var topicsAfterContentChange = await client.GetFromJsonAsync<JsonElement[]>("/api/grammar/topics");
            Assert.Equal(2, topicsAfterContentChange?.Length);
            Assert.Equal(
                new[] { "present-simple-to-be", "past-simple-new" },
                topicsAfterContentChange?.Select(topic => topic.GetProperty("slug").GetString()));
            var addedTopic = await client.GetFromJsonAsync<JsonElement>("/api/grammar/topics/past-simple-new");
            Assert.Equal("Past Simple", addedTopic.GetProperty("title").GetString());
            Assert.Single(addedTopic.GetProperty("theorySections").EnumerateArray());
            var newTopicExercises = await client.GetFromJsonAsync<JsonElement[]>(
                "/api/grammar/topics/past-simple-new/exercises");
            var discoveredExercises = newTopicExercises ?? throw new InvalidOperationException(
                "The newly added topic should return an exercise list.");
            Assert.Single(discoveredExercises);
            Assert.Equal(newExerciseId, discoveredExercises[0].GetProperty("id").GetGuid());
            Guid existingTopicId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<GrammarDbContext>();
                existingTopicId = await dbContext.Topics.Where(topic => topic.Id != newTopicId).Select(topic => topic.Id).SingleAsync();
                dbContext.ExerciseTopics.Add(new Api.Grammar.Persistence.Entities.ExerciseTopicEntity
                {
                    ExerciseId = newExerciseId,
                    TopicId = existingTopicId,
                });
                await dbContext.SaveChangesAsync();
            }
            var ambiguousTopicSubmission = await client.PostAsJsonAsync($"/api/grammar/exercises/{newExerciseId}/submit", new
            {
                submissionId = Guid.NewGuid(),
                blankAnswers = new[] { new { blankKey = "1", answer = "worked" } },
                questionAnswers = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.BadRequest, ambiguousTopicSubmission.StatusCode);
            var attributedSubmissionId = Guid.NewGuid();
            var attributedSubmission = await client.PostAsJsonAsync($"/api/grammar/exercises/{newExerciseId}/submit", new
            {
                submissionId = attributedSubmissionId,
                topicId = newTopicId,
                blankAnswers = new[] { new { blankKey = "1", answer = "worked" } },
                questionAnswers = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.OK, attributedSubmission.StatusCode);
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<GrammarDbContext>();
                var saved = await dbContext.Submissions.SingleAsync(x => x.Id == attributedSubmissionId);
                Assert.Equal(newTopicId, saved.TopicId);
            }

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<GrammarDbContext>();
                var inactiveTopic = await dbContext.Topics.SingleAsync(topic => topic.Id == newTopicId);
                inactiveTopic.IsActive = false;
                await dbContext.SaveChangesAsync();
            }

            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync("/api/grammar/topics/past-simple-new")).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync("/api/grammar/topics/past-simple-new/exercises")).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync("/api/grammar/topics/does-not-exist")).StatusCode);
            var activeTopics = await client.GetFromJsonAsync<JsonElement[]>("/api/grammar/topics");
            Assert.Single(activeTopics!);
        }
    }
}
