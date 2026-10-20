using Api.Grammar.Persistence;
using Api.Grammar.Persistence.Entities;
using Api.Grammar.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AudioApi.Tests.Grammar;

public sealed class GrammarReviewSummaryServiceTests
{
    [Fact]
    public async Task Summary_counts_only_evaluated_session_answers_and_enables_comparison_at_five_prior_answers()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GrammarDbContext>().UseSqlite(connection).Options;
        await using var db = new GrammarDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var topic = await db.Topics.SingleAsync();
        var otherTopicId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var fourEvidenceSession = CreateSession(db, now.AddDays(-10), topic.Id, 3);
        var fiveEvidenceSession = CreateSession(db, now.AddDays(-1), topic.Id, 3);

        for (var i = 0; i < 4; i++)
            AddAnswer(db, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), topic.Id, topic.Title,
                now.AddDays(-20 + i), null, i != 3);
        AddAnswer(db, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), topic.Id, topic.Title,
            now.AddDays(-5), null, false); // the fifth prior answer is after the first session began
        for (var i = 0; i < 8; i++)
            AddAnswer(db, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), otherTopicId, "Unrelated topic",
                now.AddDays(-15 + i), null, true);
        AddAnswer(db, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), topic.Id, topic.Title,
            now.AddDays(1), null, true); // future relative to both sessions; excluded from comparison

        var completedAt = now.AddDays(2);
        var completedExerciseId = Guid.NewGuid();
        AddAnswer(db, Guid.NewGuid(), Guid.NewGuid(), completedExerciseId, topic.Id, topic.Title,
            completedAt, fourEvidenceSession.Id, true, answerItems: 4, correctItems: 3);
        AddAnswer(db, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), topic.Id, topic.Title,
            completedAt.AddMinutes(1), fiveEvidenceSession.Id, true);
        await db.SaveChangesAsync();

        var service = new GrammarReviewSummaryService(db);
        var unsupported = await service.GetSummaryAsync(fourEvidenceSession.Id, CancellationToken.None);
        Assert.NotNull(unsupported);
        Assert.Equal(1, unsupported.ExercisesAnswered);
        Assert.Equal(3, unsupported.CorrectAnswers);
        Assert.Equal(1, unsupported.IncorrectAnswers);
        Assert.Equal(75m, unsupported.AccuracyPercentage);
        Assert.False(unsupported.PriorComparisonAvailable);
        Assert.Null(unsupported.PriorAccuracyPercentage);
        var topicResult = Assert.Single(unsupported.TopicResults);
        Assert.Equal(topic.Id, topicResult.TopicId);
        Assert.Equal(3, topicResult.CorrectAnswers);
        Assert.Equal(1, topicResult.IncorrectAnswers);
        Assert.True(topicResult.NeedsAttention);

        var supported = await service.GetSummaryAsync(fiveEvidenceSession.Id, CancellationToken.None);
        Assert.NotNull(supported);
        Assert.True(supported.PriorComparisonAvailable);
        Assert.Equal(60m, supported.PriorAccuracyPercentage);
        Assert.Equal(1, supported.ExercisesAnswered);
        Assert.Single(supported.TopicResults);
    }

    [Fact]
    public async Task Summary_for_unanswered_session_has_no_attempts_or_prior_comparison()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GrammarDbContext>().UseSqlite(connection).Options;
        await using var db = new GrammarDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var id = Guid.NewGuid();
        db.ReviewSessions.Add(new GrammarReviewSessionEntity
        {
            Id = id, CreatedAt = DateTimeOffset.UtcNow, SelectionSeed = 2, RequestedExerciseCount = 10,
            AvailableExerciseCount = 1,
            Exercises = { new GrammarReviewExerciseEntity
            {
                Id = Guid.NewGuid(), SessionId = id, ExerciseId = Guid.NewGuid(), TopicId = Guid.NewGuid(),
                SelectionCategory = "exploration", Reason = "No answer yet", DisplayOrder = 0,
                TitleSnapshot = "Planned", TopicTitleSnapshot = "Topic"
            } }
        });
        await db.SaveChangesAsync();

        var result = await new GrammarReviewSummaryService(db).GetSummaryAsync(id, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(0, result.ExercisesAnswered);
        Assert.Equal(0, result.CorrectAnswers);
        Assert.Equal(0, result.IncorrectAnswers);
        Assert.Null(result.AccuracyPercentage);
        Assert.Empty(result.TopicResults);
        Assert.False(result.PriorComparisonAvailable);
        Assert.Null(result.PriorAccuracyPercentage);
    }

    [Fact]
    public async Task Summary_returns_null_for_unknown_session_id()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GrammarDbContext>().UseSqlite(connection).Options;
        await using var db = new GrammarDbContext(options);
        await db.Database.EnsureCreatedAsync();

        Assert.Null(await new GrammarReviewSummaryService(db).GetSummaryAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static GrammarReviewSessionEntity CreateSession(GrammarDbContext db, DateTimeOffset createdAt,
        Guid topicId, int plannedExerciseCount)
    {
        var session = new GrammarReviewSessionEntity
        {
            Id = Guid.NewGuid(), CreatedAt = createdAt, SelectionSeed = 4, RequestedExerciseCount = 10,
            AvailableExerciseCount = plannedExerciseCount
        };
        for (var i = 0; i < plannedExerciseCount; i++)
            session.Exercises.Add(new GrammarReviewExerciseEntity
            {
                Id = Guid.NewGuid(), SessionId = session.Id, ExerciseId = Guid.NewGuid(), TopicId = topicId,
                SelectionCategory = "maintenance", Reason = "Planned exercise", DisplayOrder = i,
                TitleSnapshot = "Planned", TopicTitleSnapshot = "Topic"
            });
        db.ReviewSessions.Add(session);
        return session;
    }

    private static void AddAnswer(GrammarDbContext db, Guid submissionId, Guid exerciseId, Guid answerId,
        Guid topicId, string topicTitle, DateTimeOffset evaluatedAt, Guid? reviewSessionId, bool isCorrect,
        int answerItems = 1, int correctItems = 1)
    {
        db.Submissions.Add(new GrammarSubmissionEntity
        {
            Id = submissionId, ExerciseId = exerciseId, ExerciseTitleSnapshot = "Exercise", TopicId = topicId,
            TopicTitleSnapshot = topicTitle, ReviewSessionId = reviewSessionId, AttemptNumber = 1,
            EvaluatedAt = evaluatedAt, Score = isCorrect ? 1 : 0, MaxScore = 1,
            Percentage = isCorrect ? 100 : 0, ExerciseExplanation = "Explanation",
            PayloadHash = Guid.NewGuid().ToString("N").PadLeft(64, (char)48)
        });
        for (var i = 0; i < answerItems; i++)
        {
            var correct = answerItems == 1 ? isCorrect : i < correctItems;
            db.EvaluatedAnswers.Add(new EvaluatedAnswerEntity
            {
                Id = answerItems == 1 ? answerId : Guid.NewGuid(), SubmissionId = submissionId,
                ItemType = "blank", ItemKey = $"item-{i}", SubmittedAnswer = "answer", CorrectAnswer = "answer",
                IsCorrect = correct, Explanation = "Explanation", Points = correct ? 1 : 0, MaxPoints = 1
            });
        }
    }
}
