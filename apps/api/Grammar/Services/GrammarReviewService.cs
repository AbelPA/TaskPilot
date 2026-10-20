using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Api.Grammar.Contracts;
using Api.Grammar.Persistence;
using Api.Grammar.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Grammar.Services;

public sealed class GrammarReviewService(GrammarDbContext dbContext)
{
    public const int StandardSessionSize = 10;
    private const int RecentAnswerWindow = 5;
    private const int EvidencePrior = 5;
    private const int RecencyWindowDays = 30;
    private const decimal PresentationPenaltyPerRepeat = 0.01m;
    private const int MaximumPresentationPenaltyCount = 5;

    public Task<GrammarReviewSessionResponse> CreateSessionAsync(CancellationToken cancellationToken) =>
        CreateSessionAsync(RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue), cancellationToken);

    // Explicit seed overload makes candidate tie-breaking reproducible in tests and controlled runs.
    public Task<GrammarReviewSessionResponse> CreateSessionAsync(int selectionSeed, CancellationToken cancellationToken) =>
        CreateSessionAsync(selectionSeed, DateTimeOffset.UtcNow, cancellationToken);

    internal async Task<GrammarReviewSessionResponse> CreateSessionAsync(
        int selectionSeed, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var topics = await dbContext.Topics.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Title, x.DisplayOrder }).ToListAsync(cancellationToken);
        var topicMap = topics.ToDictionary(x => x.Id);
        var activeTopicIds = topicMap.Keys.ToArray();

        var answerHistory = await dbContext.EvaluatedAnswers.AsNoTracking()
            .Select(x => new { x.Id, x.IsCorrect, x.SubmissionId, x.Submission!.ExerciseId,
                x.Submission.TopicId, x.Submission.EvaluatedAt })
            .ToListAsync(cancellationToken);
        var topicEvidence = answerHistory.Where(x => topicMap.ContainsKey(x.TopicId))
            .GroupBy(x => x.TopicId).ToDictionary(x => x.Key,
                x => CalculateEvidence(x.Select(row => new EvidenceAnswer(row.Id, row.SubmissionId, row.IsCorrect, row.EvaluatedAt)).ToArray(), createdAt));
        var exerciseEvidence = answerHistory.GroupBy(x => x.ExerciseId)
            .ToDictionary(x => x.Key, x => CalculateEvidence(x.Select(row => new EvidenceAnswer(row.Id, row.SubmissionId, row.IsCorrect, row.EvaluatedAt)).ToArray(), createdAt));

        var presentations = await dbContext.ReviewExercises.AsNoTracking()
            .Select(x => new { x.ExerciseId, CreatedAt = x.Session!.CreatedAt })
            .ToListAsync(cancellationToken);
        var presentationEvidence = presentations.GroupBy(x => x.ExerciseId)
            .ToDictionary(x => x.Key, x => new PresentationEvidence(x.Count()));

        var exerciseTopicLinks = await dbContext.Exercises.AsNoTracking().Where(x => x.IsActive)
            .SelectMany(x => x.ExerciseTopics.Where(link => activeTopicIds.Contains(link.TopicId)),
                (exercise, link) => new { exercise.Id, exercise.Title, link.TopicId })
            .ToListAsync(cancellationToken);
        // One candidate per exercise. Multi-topic links resolve to the strongest active topic evidence;
        // catalog display order and then topic ID provide a stable tie-break independent of database/link enumeration order.
        var assignedExercises = exerciseTopicLinks.GroupBy(x => x.Id).Select(group => group
            .OrderByDescending(x => topicEvidence.TryGetValue(x.TopicId, out var evidence) ? evidence.Score : 0.5m)
            .ThenBy(x => topicMap[x.TopicId].DisplayOrder).ThenBy(x => x.TopicId).First()).ToArray();

        var candidates = assignedExercises.Select(x =>
        {
            topicEvidence.TryGetValue(x.TopicId, out var topic);
            exerciseEvidence.TryGetValue(x.Id, out var exercise);
            presentationEvidence.TryGetValue(x.Id, out var presentation);
            var basis = exercise is { Count: > 0 } ? exercise : topic ?? Evidence.Neutral;
            var repeatPenalty = PresentationPenaltyPerRepeat * Math.Min(presentation?.Count ?? 0, MaximumPresentationPenaltyCount);
            var selectionScore = Math.Clamp(basis.Score - repeatPenalty, 0m, 1m);
            var topicCategory = topic is null || topic.Count == 0 ? "exploration"
                : topic.Score >= 0.55m ? "priority" : "maintenance";
            var reason = topicCategory switch
            {
                "exploration" => $"Explore this active topic; no evaluated answers are recorded yet; presented {presentation?.Count ?? 0} time(s).",
                "priority" when exercise is { Count: > 0 } =>
                    $"Priority topic: {topic!.IncorrectCount} incorrect answer(s) among {topic.Count} evaluated. This exercise: {exercise.IncorrectCount} incorrect answer(s) among {exercise.Count} evaluated; presented {presentation?.Count ?? 0} time(s).",
                "priority" =>
                    $"Prioritized from {topic!.IncorrectCount} incorrect answer(s) among {topic.Count} evaluated for this topic; presented {presentation?.Count ?? 0} time(s).",
                _ when exercise is { Count: > 0 } =>
                    $"Maintenance practice based on {exercise.Count} evaluated answer(s) for this exercise; presented {presentation?.Count ?? 0} time(s).",
                _ => $"Maintenance practice based on {topic!.Count} evaluated answer(s) for this topic; presented {presentation?.Count ?? 0} time(s)."
            };
            return new Candidate(x.Id, x.Title, x.TopicId, topicMap[x.TopicId].Title, topicCategory,
                selectionScore, reason, presentation?.Count ?? 0);
        }).OrderByDescending(x => x.SelectionScore)
            .ThenBy(x => StableTieBreak(selectionSeed, x.ExerciseId), StringComparer.Ordinal).ToList();

        var chosen = new List<Candidate>();
        var used = new HashSet<Guid>();
        var perCategory = new[] { (Name: "priority", Count: 7), (Name: "maintenance", Count: 2), (Name: "exploration", Count: 1) };
        foreach (var allocation in perCategory)
        {
            var remaining = allocation.Count;
            foreach (var diverse in new[] { true, false })
            {
                foreach (var item in candidates.Where(x => x.Category == allocation.Name && !used.Contains(x.ExerciseId)
                    && (!diverse || chosen.All(y => y.TopicId != x.TopicId))).Take(remaining).ToArray())
                { chosen.Add(item); used.Add(item.ExerciseId); remaining--; }
                if (remaining == 0) break;
            }
        }
        foreach (var item in candidates.Where(x => !used.Contains(x.ExerciseId)))
        {
            if (chosen.Count >= StandardSessionSize) break;
            chosen.Add(item); used.Add(item.ExerciseId);
        }
        var session = new GrammarReviewSessionEntity
        {
            Id = Guid.NewGuid(), CreatedAt = createdAt, SelectionSeed = selectionSeed,
            RequestedExerciseCount = StandardSessionSize, AvailableExerciseCount = chosen.Count
        };
        var selectedExercises = chosen.Select((x, index) => new GrammarReviewExerciseEntity
        {
            Id = Guid.NewGuid(), SessionId = session.Id, ExerciseId = x.ExerciseId, TopicId = x.TopicId,
            SelectionCategory = x.Category, PriorityScore = x.Category == "priority" ? x.SelectionScore : null,
            Reason = x.Reason, DisplayOrder = index, TitleSnapshot = x.Title, TopicTitleSnapshot = x.TopicTitle
        }).ToArray();
        session.Exercises = selectedExercises;
        dbContext.ReviewSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new GrammarReviewSessionResponse(session.Id, StandardSessionSize, chosen.Count,
            chosen.Count < StandardSessionSize, selectedExercises.Select(x => new GrammarReviewExerciseResponse(x.ExerciseId,
                x.TopicId, x.TitleSnapshot, x.TopicTitleSnapshot, x.SelectionCategory, x.PriorityScore, x.Reason, x.DisplayOrder)).ToArray());
    }

    private static Evidence CalculateEvidence(EvidenceAnswer[] answers, DateTimeOffset now)
    {
        if (answers.Length == 0) return Evidence.Neutral;
        var ordered = answers.OrderByDescending(x => x.EvaluatedAt).ThenByDescending(x => x.SubmissionId)
            .ThenByDescending(x => x.AnswerId).ToArray();
        var count = ordered.Length;
        var incorrect = ordered.Count(x => !x.IsCorrect);
        var recent = ordered.Take(RecentAnswerWindow).ToArray();
        var historicalError = incorrect / (decimal)count;
        var recentError = recent.Count(x => !x.IsCorrect) / (decimal)recent.Length;
        var shrinkHistorical = count / (count + (decimal)EvidencePrior);
        var shrinkRecent = recent.Length / (recent.Length + (decimal)EvidencePrior);
        var recency = Math.Clamp((decimal)(now - ordered[0].EvaluatedAt).TotalDays / RecencyWindowDays, 0m, 1m);
        var score = 0.50m * (0.50m + (historicalError - 0.50m) * shrinkHistorical)
            + 0.30m * (0.50m + (recentError - 0.50m) * shrinkRecent)
            + 0.20m * recency;
        return new Evidence(count, incorrect, Math.Clamp(score, 0m, 1m));
    }

    private static string StableTieBreak(int seed, Guid exerciseId) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Concat(seed.ToString(CultureInfo.InvariantCulture), ":", exerciseId.ToString("N")))));

    private sealed record EvidenceAnswer(Guid AnswerId, Guid SubmissionId, bool IsCorrect, DateTimeOffset EvaluatedAt);
    private sealed record Evidence(int Count, int IncorrectCount, decimal Score)
    {
        public static Evidence Neutral { get; } = new(0, 0, 0.5m);
    }
    private sealed record PresentationEvidence(int Count);
    private sealed record Candidate(Guid ExerciseId, string Title, Guid TopicId, string TopicTitle,
        string Category, decimal SelectionScore, string Reason, int PresentationCount);
}
