namespace Api.Grammar.Contracts;

public sealed record GrammarExerciseResult(
    Guid ExerciseId, decimal Score, decimal MaxScore, decimal Percentage, string Explanation,
    IReadOnlyList<GrammarExerciseItemResult> Results, Guid SubmissionId = default, int AttemptNumber = 1);

public sealed record GrammarExerciseItemResult(
    string ItemKey, string ItemType, string SubmittedAnswer, bool IsCorrect, string CorrectAnswer, string Explanation, decimal Points);
