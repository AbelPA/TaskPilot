using Api.Grammar.Contracts;
using Api.Grammar.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Grammar.Services;

public sealed class GrammarReviewSummaryService(GrammarDbContext dbContext)
{
    public async Task<GrammarReviewSessionSummaryResponse?> GetSummaryAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.ReviewSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        var answers = await dbContext.EvaluatedAnswers.AsNoTracking()
            .Where(x => dbContext.Submissions.Any(s => s.Id == x.SubmissionId && s.ReviewSessionId == sessionId))
            .Select(x => new { x.IsCorrect, x.SubmissionId, x.Submission!.ExerciseId, x.Submission.TopicId,
                x.Submission.TopicTitleSnapshot, x.Submission.EvaluatedAt })
            .ToListAsync(cancellationToken);
        var correct = answers.Count(x => x.IsCorrect);
        var incorrect = answers.Count - correct;
        var groups = answers.GroupBy(x => new { x.TopicId, x.TopicTitleSnapshot })
            .Select(x =>
            {
                var right = x.Count(a => a.IsCorrect);
                var wrong = x.Count() - right;
                return new GrammarReviewTopicSummary(x.Key.TopicId, x.Key.TopicTitleSnapshot, right, wrong,
                    wrong > 0);
            }).OrderByDescending(x => x.NeedsAttention).ThenBy(x => x.TopicTitle).ToArray();
        var topicIds = answers.Select(x => x.TopicId).Distinct().ToArray();
        var priorRows = topicIds.Length == 0 ? [] : await dbContext.EvaluatedAnswers.AsNoTracking()
            .Select(x => new { x.IsCorrect, x.Submission!.TopicId, x.Submission.EvaluatedAt }).ToListAsync(cancellationToken);
        var prior = priorRows.Where(x => x.EvaluatedAt < session.CreatedAt && topicIds.Contains(x.TopicId))
            .Select(x => x.IsCorrect).ToArray();
        var priorAvailable = prior.Length >= 5;
        decimal? priorAccuracy = priorAvailable ? Math.Round(prior.Count(x => x) * 100m / prior.Length, 1, MidpointRounding.AwayFromZero) : null;
        decimal? percentage = answers.Count == 0 ? null : Math.Round(correct * 100m / answers.Count, 1, MidpointRounding.AwayFromZero);
        return new GrammarReviewSessionSummaryResponse(sessionId, answers.Select(x => x.ExerciseId).Distinct().Count(),
            correct, incorrect, percentage, groups, priorAvailable, priorAccuracy);
    }
}
