using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Grammar.Contracts;
using Api.Grammar.Persistence;
using Api.Grammar.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Grammar.Services;

public sealed class GrammarSubmissionHistoryService(GrammarDbContext dbContext)
{
    public async Task<GrammarExerciseResult?> FindReplayAsync(Guid exerciseId, GrammarExerciseSubmission request, CancellationToken cancellationToken)
    {
        var id = request.SubmissionId;
        if (id == Guid.Empty) throw new GrammarSubmissionException("submissionId must be a non-empty UUID.");
        var previous = await dbContext.Submissions.AsNoTracking().Include(x => x.Answers)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (previous is null) return null;
        if (previous.ExerciseId != exerciseId || previous.PayloadHash != Hash(exerciseId, request))
            throw new GrammarSubmissionConflictException("The submissionId was already used with different input.");
        return ToResult(previous);
    }

    public async Task<GrammarExerciseResult> PersistAsync(ExerciseEntity exercise, GrammarExerciseSubmission request,
        GrammarExerciseResult result, CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await PersistOnceAsync(exercise, request, result, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsSerializationFailure(ex))
            {
                // Concurrent serializable submissions can be aborted by PostgreSQL; the work is safe to redo.
                foreach (var entry in dbContext.ChangeTracker.Entries<GrammarSubmissionEntity>().ToArray())
                    entry.State = EntityState.Detached;
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsSerializationFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException!)
            if (current is Npgsql.PostgresException { SqlState: "40001" }) return true;
        return false;
    }

    private async Task<GrammarExerciseResult> PersistOnceAsync(ExerciseEntity exercise, GrammarExerciseSubmission request,
        GrammarExerciseResult result, CancellationToken cancellationToken)
    {
        var topicLinks = exercise.ExerciseTopics.ToArray();
        var topicId = request.TopicId ?? (topicLinks.Length == 1 ? topicLinks[0].TopicId : Guid.Empty);
        var selectedTopic = topicLinks.SingleOrDefault(x => x.TopicId == topicId)?.Topic;
        if (selectedTopic is null)
            throw new GrammarSubmissionException("Provide a topic associated with this exercise.");
        if (request.ReviewSessionId is Guid sessionId &&
            !await dbContext.ReviewExercises.AnyAsync(x => x.SessionId == sessionId && x.ExerciseId == exercise.Id && x.TopicId == topicId, cancellationToken))
            throw new GrammarSubmissionException("The exercise and topic do not belong to the specified review session.");

        var submissionId = request.SubmissionId;
        if (submissionId == Guid.Empty) throw new GrammarSubmissionException("submissionId must be a non-empty UUID.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await dbContext.Submissions.AsNoTracking().Include(x => x.Answers)
            .SingleOrDefaultAsync(x => x.Id == submissionId, cancellationToken);
        var requestHash = Hash(exercise.Id, request);
        if (existing is not null)
        {
            if (existing.ExerciseId != exercise.Id || existing.PayloadHash != requestHash)
                throw new GrammarSubmissionConflictException("The submissionId was already used with different input.");
            await transaction.CommitAsync(cancellationToken);
            return ToResult(existing);
        }

        var attemptNumber = (await dbContext.Submissions
            .Where(x => x.ExerciseId == exercise.Id && x.TopicId == topicId)
            .Select(x => (int?)x.AttemptNumber).MaxAsync(cancellationToken) ?? 0) + 1;
        var entity = new GrammarSubmissionEntity
        {
            Id = submissionId, ExerciseId = exercise.Id, ExerciseTitleSnapshot = exercise.Title,
            TopicId = topicId, TopicTitleSnapshot = selectedTopic.Title, ReviewSessionId = request.ReviewSessionId,
            AttemptNumber = attemptNumber, EvaluatedAt = DateTimeOffset.UtcNow, Score = result.Score,
            MaxScore = result.MaxScore, Percentage = result.Percentage, ExerciseExplanation = result.Explanation,
            PayloadHash = requestHash
        };
        foreach (var item in result.Results)
        {
            var maxPoints = item.ItemType == "blank"
                ? exercise.Blanks.Single(x => x.BlankKey == item.ItemKey).Points
                : exercise.Questions.Single(x => x.QuestionKey == item.ItemKey).Points;
            entity.Answers.Add(new EvaluatedAnswerEntity
            {
                Id = Guid.NewGuid(), SubmissionId = submissionId, ItemType = item.ItemType, ItemKey = item.ItemKey,
                SubmittedAnswer = item.SubmittedAnswer, CorrectAnswer = item.CorrectAnswer, IsCorrect = item.IsCorrect,
                Explanation = item.Explanation, Points = item.Points, MaxPoints = maxPoints
            });
        }
        dbContext.Submissions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result with { SubmissionId = submissionId, AttemptNumber = attemptNumber };
    }

    private static string Hash(Guid exerciseId, GrammarExerciseSubmission request)
    {
        var normalized = new
        {
            exerciseId,
            request.TopicId,
            request.ReviewSessionId,
            blanks = (request.BlankAnswers ?? []).OrderBy(x => x.BlankKey, StringComparer.Ordinal).ToArray(),
            questions = (request.QuestionAnswers ?? []).OrderBy(x => x.QuestionKey, StringComparer.Ordinal).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized))));
    }

    private static GrammarExerciseResult ToResult(GrammarSubmissionEntity row) => new(
        row.ExerciseId, row.Score, row.MaxScore, row.Percentage, row.ExerciseExplanation,
        row.Answers.OrderBy(x => x.ItemType).ThenBy(x => x.ItemKey, StringComparer.Ordinal)
            .Select(x => new GrammarExerciseItemResult(x.ItemKey, x.ItemType, x.SubmittedAnswer, x.IsCorrect,
                x.CorrectAnswer, x.Explanation, x.Points)).ToArray(), row.Id, row.AttemptNumber);
}

public sealed class GrammarSubmissionConflictException(string message) : Exception(message);
