using Api.Grammar.Persistence;
using Api.Grammar.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AudioApi.Tests.Grammar;

public sealed class GrammarDbContextTests
{
    [Fact]
    public async Task EnsureCreated_seeds_cefr_topic_theory_and_both_exercise_types()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GrammarDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new GrammarDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        Assert.Equal("A1", await dbContext.Topics.Select(topic => topic.Level).SingleAsync());
        Assert.Single(await dbContext.TheorySections.ToListAsync());
        Assert.Equal(2, await dbContext.Exercises.CountAsync());
        Assert.Equal(2, await dbContext.ExerciseTopics.CountAsync());
        Assert.Equal(2, await dbContext.Blanks.CountAsync());
        Assert.Equal(1, await dbContext.Questions.CountAsync());
    }

    [Fact]
    public async Task Evaluated_history_is_retained_and_duplicate_item_keys_are_rejected()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GrammarDbContext>().UseSqlite(connection).Options;
        await using var db = new GrammarDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var id = Guid.NewGuid();
        db.Submissions.Add(new GrammarSubmissionEntity
        {
            Id = id, ExerciseId = Guid.NewGuid(), ExerciseTitleSnapshot = "Historic exercise", TopicId = Guid.NewGuid(),
            TopicTitleSnapshot = "Historic topic", AttemptNumber = 1, EvaluatedAt = DateTimeOffset.UtcNow,
            Score = 1, MaxScore = 1, Percentage = 100, ExerciseExplanation = "Saved result", PayloadHash = new string((char)65, 64)
        });
        db.EvaluatedAnswers.Add(new EvaluatedAnswerEntity
        {
            Id = Guid.NewGuid(), SubmissionId = id, ItemType = "blank", ItemKey = "1", SubmittedAnswer = "is",
            CorrectAnswer = "is", IsCorrect = true, Explanation = "Correct", Points = 1, MaxPoints = 1
        });
        await db.SaveChangesAsync();
        db.EvaluatedAnswers.Add(new EvaluatedAnswerEntity
        {
            Id = Guid.NewGuid(), SubmissionId = id, ItemType = "blank", ItemKey = "1", SubmittedAnswer = "was",
            CorrectAnswer = "is", IsCorrect = false, Explanation = "Incorrect", Points = 0, MaxPoints = 1
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal("Historic exercise", (await db.Submissions.SingleAsync()).ExerciseTitleSnapshot);
        Assert.Single(await db.EvaluatedAnswers.ToListAsync());
    }
}
