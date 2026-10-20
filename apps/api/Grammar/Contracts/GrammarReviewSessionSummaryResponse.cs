namespace Api.Grammar.Contracts;

public sealed record GrammarReviewSessionSummaryResponse(Guid SessionId, int ExercisesAnswered, int CorrectAnswers,
    int IncorrectAnswers, decimal? AccuracyPercentage, IReadOnlyList<GrammarReviewTopicSummary> TopicResults,
    bool PriorComparisonAvailable, decimal? PriorAccuracyPercentage);
public sealed record GrammarReviewTopicSummary(Guid TopicId, string TopicTitle, int CorrectAnswers,
    int IncorrectAnswers, bool NeedsAttention);
