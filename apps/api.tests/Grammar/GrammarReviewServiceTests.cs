using Api.Grammar.Persistence;
using Api.Grammar.Persistence.Entities;
using Api.Grammar.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AudioApi.Tests.Grammar;

public sealed class GrammarReviewServiceTests
{
    [Fact]
    public async Task Review_is_persisted_unique_bounded_and_explores_when_no_history_exists()
    {
        await using var db = await CreateDatabaseAsync();
        var result = await new GrammarReviewService(db).CreateSessionAsync(CancellationToken.None);
        Assert.Equal(10, result.StandardExerciseCount);
        Assert.Equal(2, result.ExerciseCount);
        Assert.True(result.IsShorterThanStandard);
        Assert.Equal(result.ExerciseCount, result.Exercises.Select(x => x.ExerciseId).Distinct().Count());
        Assert.All(result.Exercises, x => Assert.Equal("exploration", x.SelectionCategory));
        Assert.All(result.Exercises, x => Assert.Null(x.PriorityScore));
        var session = await db.ReviewSessions.Include(x => x.Exercises).SingleAsync();
        Assert.Equal(result.SessionId, session.Id);
        Assert.Equal(2, session.AvailableExerciseCount);
        Assert.Equal(new[] { 0, 1 }, session.Exercises.OrderBy(x => x.DisplayOrder).Select(x => x.DisplayOrder));
    }

    [Fact]
    public async Task Priority_score_uses_weighted_shrunk_historical_recent_and_recency_evidence()
    {
        await using var db = await CreateDatabaseAsync();
        var topic = await db.Topics.SingleAsync();
        var exercise = await db.Exercises.OrderBy(x => x.DisplayOrder).FirstAsync();
        var now = DateTimeOffset.UtcNow;
        // Seven of ten historical answers are incorrect; four of the most recent five are incorrect.
        for (var i = 0; i < 10; i++)
        {
            var submissionId = Guid.NewGuid();
            var isCorrect = i == 0 || i == 1 || i == 9;
            AddHistory(db, submissionId, exercise.Id, topic.Id, i + 1, now.AddDays(-55 + i), isCorrect);
        }
        await db.SaveChangesAsync();

        var result = await new GrammarReviewService(db).CreateSessionAsync(CancellationToken.None);
        Assert.Equal(2, result.ExerciseCount);
        Assert.All(result.Exercises, item =>
        {
            Assert.Equal("priority", item.SelectionCategory);
            Assert.NotNull(item.PriorityScore);
            // historical: .50 * (.50 + (.70 - .50) * 10/15) = .3167
            // recent: .30 * (.50 + (.80 - .50) * 5/10) = .195
            // recency: .20 * 1 (last attempt is over 30 days old)
            Assert.InRange(item.PriorityScore!.Value, .711m, .712m);
            Assert.Contains("among 10 evaluated", item.Reason);
        });
        var persisted = await db.ReviewExercises.Where(x => x.SessionId == result.SessionId).ToListAsync();
        Assert.All(persisted, item => Assert.Equal(item.PriorityScore, result.Exercises.Single(x => x.ExerciseId == item.ExerciseId).PriorityScore));
    }

    [Fact]
    public async Task Exercise_performance_overrides_topic_fallback_when_item_history_exists()
    {
        await using var db = await CreateDatabaseAsync();
        var topic = await db.Topics.SingleAsync();
        var exercises = await db.Exercises.OrderBy(x => x.DisplayOrder).ToArrayAsync();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 10; i++)
        {
            var exercise = i < 5 ? exercises[0] : exercises[1];
            AddHistory(db, Guid.NewGuid(), exercise.Id, topic.Id, i + 1,
                now.AddDays(-55 + i), i < 5);
        }
        await db.SaveChangesAsync();

        var result = await new GrammarReviewService(db).CreateSessionAsync(42, CancellationToken.None);
        Assert.Equal("priority", result.Exercises[0].SelectionCategory);
        Assert.Equal(exercises[1].Id, result.Exercises[0].ExerciseId);
        Assert.True(result.Exercises[0].PriorityScore > result.Exercises[1].PriorityScore);
        Assert.Contains("5 incorrect answer(s) among 5 evaluated", result.Exercises[0].Reason);
        Assert.Contains("0 incorrect answer(s) among 5 evaluated", result.Exercises[1].Reason);
    }

    [Fact]
    public async Task Stable_seed_produces_the_same_tie_order_and_is_persisted()
    {
        await using var db1 = await CreateDatabaseAsync();
        await using var db2 = await CreateDatabaseAsync();
        var first = await new GrammarReviewService(db1).CreateSessionAsync(123456, new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), CancellationToken.None);
        var second = await new GrammarReviewService(db2).CreateSessionAsync(123456, CancellationToken.None);

        Assert.Equal(first.Exercises.Select(x => x.ExerciseId), second.Exercises.Select(x => x.ExerciseId));
        Assert.Equal(123456, (await db1.ReviewSessions.SingleAsync()).SelectionSeed);
        Assert.Equal(123456, (await db2.ReviewSessions.SingleAsync()).SelectionSeed);
    }

    [Fact]
    public async Task Prior_presentations_reduce_repeat_preference_when_same_topic_alternatives_exist()
    {
        await using var db = await CreateDatabaseAsync();
        var topic = await db.Topics.SingleAsync();
        var exercises = await db.Exercises.OrderBy(x => x.DisplayOrder).ToArrayAsync();
        for (var i = 0; i < 5; i++)
        {
            var prior = new GrammarReviewSessionEntity
            {
                Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow.AddDays(-10 + i), SelectionSeed = i,
                RequestedExerciseCount = 10, AvailableExerciseCount = 1
            };
            prior.Exercises.Add(new GrammarReviewExerciseEntity
            {
                Id = Guid.NewGuid(), SessionId = prior.Id, ExerciseId = exercises[0].Id, TopicId = topic.Id,
                SelectionCategory = "exploration", Reason = "previous presentation", DisplayOrder = 0,
                TitleSnapshot = exercises[0].Title, TopicTitleSnapshot = topic.Title
            });
            db.ReviewSessions.Add(prior);
        }
        await db.SaveChangesAsync();

        var result = await new GrammarReviewService(db).CreateSessionAsync(7, CancellationToken.None);
        Assert.Equal(exercises[1].Id, result.Exercises[0].ExerciseId);
        Assert.Contains("presented 5 time(s)", result.Exercises.Single(x => x.ExerciseId == exercises[0].Id).Reason);
    }

    [Fact]
    public async Task Multi_topic_exercise_is_attributed_once_to_the_stable_best_topic()
    {
        await using var db = await CreateDatabaseAsync();
        var topics = await db.Topics.OrderBy(x => x.Id).ToArrayAsync();
        var multiTopicExercise = await db.Exercises.OrderBy(x => x.DisplayOrder).FirstAsync();
        var secondTopic = AddTopic(db, "Second linked topic");
        db.ExerciseTopics.Add(new ExerciseTopicEntity { ExerciseId = multiTopicExercise.Id, TopicId = secondTopic.Id });
        await db.SaveChangesAsync();

        var selected = await new GrammarReviewService(db).CreateSessionAsync(99, CancellationToken.None);
        var assignment = Assert.Single(selected.Exercises, x => x.ExerciseId == multiTopicExercise.Id);
        Assert.Equal(topics[0].Id, assignment.TopicId);
        Assert.Single(await db.ReviewExercises.Where(x => x.SessionId == selected.SessionId && x.ExerciseId == multiTopicExercise.Id).ToListAsync());
    }

    [Fact]
    public async Task Small_sample_is_shrunk_and_does_not_claim_demonstrated_difficulty()
    {
        await using var db = await CreateDatabaseAsync();
        var topic = await db.Topics.SingleAsync();
        var exercise = await db.Exercises.FirstAsync();
        AddHistory(db, Guid.NewGuid(), exercise.Id, topic.Id, 1, DateTimeOffset.UtcNow, false);
        await db.SaveChangesAsync();

        var result = await new GrammarReviewService(db).CreateSessionAsync(CancellationToken.None);
        Assert.All(result.Exercises, item =>
        {
            Assert.Equal("maintenance", item.SelectionCategory);
            Assert.Null(item.PriorityScore);
            Assert.Contains("1 evaluated answer(s)", item.Reason);
            Assert.DoesNotContain("prioritized", item.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Category_targets_use_available_alternatives_and_return_ten_unique_exercises()
    {
        await using var db = await CreateDatabaseAsync();
        var priorityTopic = await db.Topics.SingleAsync();
        var priorityExercise = await db.Exercises.FirstAsync();
        for (var i = 0; i < 10; i++)
            AddHistory(db, Guid.NewGuid(), priorityExercise.Id, priorityTopic.Id, i + 1,
                DateTimeOffset.UtcNow.AddDays(-45 + i), false);

        var maintenanceTopic = AddTopic(db, "Maintenance");
        var explorationTopic = AddTopic(db, "Exploration");
        AddExercises(db, priorityTopic, 5, "Priority alternative"); // two seeded + five = seven
        AddExercises(db, maintenanceTopic, 2, "Maintenance alternative");
        AddExercises(db, explorationTopic, 3, "Exploration alternative");
        var maintenanceExercise = db.Exercises.Local.Single(x => x.Title == "Maintenance alternative 1");
        AddHistory(db, Guid.NewGuid(), maintenanceExercise.Id, maintenanceTopic.Id, 1, DateTimeOffset.UtcNow, true);
        await db.SaveChangesAsync();

        var result = await new GrammarReviewService(db).CreateSessionAsync(CancellationToken.None);
        Assert.Equal(10, result.ExerciseCount);
        Assert.False(result.IsShorterThanStandard);
        Assert.Equal(10, result.Exercises.Select(x => x.ExerciseId).Distinct().Count());
        Assert.Equal(7, result.Exercises.Count(x => x.SelectionCategory == "priority"));
        Assert.Equal(2, result.Exercises.Count(x => x.SelectionCategory == "maintenance"));
        Assert.Single(result.Exercises, x => x.SelectionCategory == "exploration");
        Assert.Equal(7, result.Exercises.Count(x => x.TopicId == priorityTopic.Id)); // fallback can use alternatives from an already represented topic
        Assert.Equal(2, result.Exercises.Count(x => x.TopicId == maintenanceTopic.Id));
        Assert.Single(result.Exercises, x => x.TopicId == explorationTopic.Id);
        Assert.All(result.Exercises.Where(x => x.SelectionCategory != "priority"), x => Assert.Null(x.PriorityScore));
        var persisted = await db.ReviewExercises.Where(x => x.SessionId == result.SessionId).ToListAsync();
        Assert.Equal(10, persisted.Count);
        Assert.Equal(Enumerable.Range(0, 10), persisted.OrderBy(x => x.DisplayOrder).Select(x => x.DisplayOrder));
    }

    private static async Task<GrammarDbContext> CreateDatabaseAsync()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GrammarDbContext>().UseSqlite(connection).Options;
        var db = new GrammarDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static GrammarTopicEntity AddTopic(GrammarDbContext db, string title)
    {
        var topic = new GrammarTopicEntity
        {
            Id = Guid.NewGuid(), Slug = title.ToLowerInvariant(), Title = title, Description = "Review test topic",
            Level = "A1", DisplayOrder = db.Topics.Count() + 1, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Topics.Add(topic);
        return topic;
    }

    private static void AddExercises(GrammarDbContext db, GrammarTopicEntity topic, int count, string titlePrefix)
    {
        for (var i = 1; i <= count; i++)
        {
            var exercise = new ExerciseEntity
            {
                Id = Guid.NewGuid(), Title = $"{titlePrefix} {i}", Instruction = "Review", ExerciseType = "fill_blank",
                ContentTemplate = "prompt", Explanation = "explanation", CaseSensitive = false,
                DisplayOrder = i, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Exercises.Add(exercise);
            db.ExerciseTopics.Add(new ExerciseTopicEntity { ExerciseId = exercise.Id, TopicId = topic.Id });
        }
    }

    internal static void AddHistory(GrammarDbContext db, Guid submissionId, Guid exerciseId, Guid topicId,
        int attemptNumber, DateTimeOffset evaluatedAt, bool isCorrect)
    {
        db.Submissions.Add(new GrammarSubmissionEntity
        {
            Id = submissionId, ExerciseId = exerciseId, ExerciseTitleSnapshot = "Historical exercise", TopicId = topicId,
            TopicTitleSnapshot = "Historical topic", AttemptNumber = attemptNumber, EvaluatedAt = evaluatedAt,
            Score = isCorrect ? 1 : 0, MaxScore = 1, Percentage = isCorrect ? 100 : 0,
            ExerciseExplanation = "Historical explanation", PayloadHash = Guid.NewGuid().ToString("N").PadLeft(64, (char)48)
        });
        db.EvaluatedAnswers.Add(new EvaluatedAnswerEntity
        {
            Id = Guid.NewGuid(), SubmissionId = submissionId, ItemType = "blank", ItemKey = "item",
            SubmittedAnswer = "submitted", CorrectAnswer = "correct", IsCorrect = isCorrect,
            Explanation = "Historical explanation", Points = isCorrect ? 1 : 0, MaxPoints = 1
        });
    }
}
