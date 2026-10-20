using Api.Grammar.Contracts;
using Api.Grammar.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Grammar.Services;

public sealed class GrammarStatisticsService(GrammarDbContext dbContext)
{
    public async Task<IReadOnlyList<GrammarTopicStatisticsResponse>> GetTopicStatisticsAsync(CancellationToken cancellationToken)
    {
        var topics = await dbContext.Topics.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Title)
            .Select(x => new { x.Id, x.Slug, x.Title }).ToListAsync(cancellationToken);
        var topicIds = topics.Select(x => x.Id).ToArray();
        var rows = await dbContext.EvaluatedAnswers.AsNoTracking()
            .Where(x => topicIds.Contains(x.Submission!.TopicId))
            .Select(x => new { x.Id, x.IsCorrect, x.SubmissionId,
                Submission = new { x.Submission!.TopicId, x.Submission.EvaluatedAt } })
            .ToListAsync(cancellationToken);
        return topics.Select(topic =>
        {
            var attempts = rows.Where(x => x.Submission.TopicId == topic.Id)
                .OrderByDescending(x => x.Submission.EvaluatedAt).ThenByDescending(x => x.SubmissionId).ThenByDescending(x => x.Id).ToArray();
            var recent = attempts.Take(5).ToArray();
            var correct = attempts.Count(x => x.IsCorrect);
            var count = attempts.Length;
            decimal? accuracy = count == 0 ? null : Math.Round(correct * 100m / count, 1, MidpointRounding.AwayFromZero);
            return new GrammarTopicStatisticsResponse(topic.Id, topic.Slug, topic.Title, count, correct, count - correct,
                accuracy, accuracy is null ? null : 100m - accuracy.Value,
                attempts.Length == 0 ? null : attempts[0].Submission.EvaluatedAt, recent.Length,
                recent.Count(x => x.IsCorrect), recent.Count(x => !x.IsCorrect), count > 0, count < 5);
        }).ToArray();
    }
}
