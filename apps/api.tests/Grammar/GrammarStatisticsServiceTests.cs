using Api.Grammar.Persistence;
using Api.Grammar.Persistence.Entities;
using Api.Grammar.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AudioApi.Tests.Grammar;

public sealed class GrammarStatisticsServiceTests
{
    [Fact]
    public async Task Statistics_include_empty_active_topics_and_use_latest_five_evaluated_answers()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GrammarDbContext>().UseSqlite(connection).Options;
        await using var db = new GrammarDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new GrammarStatisticsService(db);
        var empty = Assert.Single(await service.GetTopicStatisticsAsync(CancellationToken.None));
        Assert.False(empty.Assessed);
        Assert.Null(empty.AccuracyPercentage);
        Assert.Null(empty.ErrorPercentage);
        Assert.Equal(0, empty.RecentAttemptCount);
        var topicId = await db.Topics.Select(x => x.Id).SingleAsync();
        var exerciseId = await db.Exercises.OrderBy(x => x.DisplayOrder).Select(x => x.Id).FirstAsync();
        var start = DateTimeOffset.UtcNow.AddDays(-7);
        for (var i = 0; i < 6; i++)
        {
            var id = Guid.NewGuid();
            db.Submissions.Add(new GrammarSubmissionEntity
            {
                Id = id, ExerciseId = exerciseId, ExerciseTitleSnapshot = "Exercise", TopicId = topicId,
                TopicTitleSnapshot = "Topic", AttemptNumber = i + 1, EvaluatedAt = start.AddDays(i),
                Score = 1, MaxScore = 1, Percentage = 100, ExerciseExplanation = "Explanation",
                PayloadHash = i.ToString("D64")
            });
            db.EvaluatedAnswers.Add(new EvaluatedAnswerEntity
            {
                Id = Guid.NewGuid(), SubmissionId = id, ItemType = "blank", ItemKey = i.ToString(),
                SubmittedAnswer = "answer", CorrectAnswer = "answer", IsCorrect = i >= 2,
                Explanation = "Explanation", Points = 1, MaxPoints = 1
            });
        }
        await db.SaveChangesAsync();
        var result = Assert.Single(await service.GetTopicStatisticsAsync(CancellationToken.None));
        Assert.Equal(6, result.TotalAttempts);
        Assert.Equal(4, result.CorrectAnswers);
        Assert.Equal(2, result.IncorrectAnswers);
        Assert.Equal(66.7m, result.AccuracyPercentage);
        Assert.Equal(33.3m, result.ErrorPercentage);
        Assert.Equal(start.AddDays(5), result.LastAttemptAt);
        Assert.Equal(5, result.RecentAttemptCount);
        Assert.Equal(4, result.RecentCorrectAnswers);
        Assert.Equal(1, result.RecentIncorrectAnswers);
        Assert.False(result.InsufficientEvidence);
    }
}
