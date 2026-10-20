namespace Api.Grammar.Contracts;

public sealed record GrammarTopicStatisticsResponse(Guid TopicId, string TopicSlug, string TopicTitle,
    int TotalAttempts, int CorrectAnswers, int IncorrectAnswers, decimal? AccuracyPercentage, decimal? ErrorPercentage,
    DateTimeOffset? LastAttemptAt, int RecentAttemptCount, int RecentCorrectAnswers, int RecentIncorrectAnswers,
    bool Assessed, bool InsufficientEvidence);
