namespace Api.Grammar.Contracts;

public sealed record GrammarReviewSessionResponse(Guid SessionId, int StandardExerciseCount, int ExerciseCount,
    bool IsShorterThanStandard, IReadOnlyList<GrammarReviewExerciseResponse> Exercises);
public sealed record GrammarReviewExerciseResponse(Guid ExerciseId, Guid TopicId, string Title, string TopicTitle,
    string SelectionCategory, decimal? PriorityScore, string Reason, int DisplayOrder);
