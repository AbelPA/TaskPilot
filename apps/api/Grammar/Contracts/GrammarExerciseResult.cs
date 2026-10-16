namespace Api.Grammar.Contracts;

public sealed record GrammarExerciseResult(
    Guid ExerciseId,
    decimal Score,
    decimal MaxScore,
    decimal Percentage,
    string Explanation,
    IReadOnlyList<GrammarExerciseItemResult> Results);

public sealed record GrammarExerciseItemResult(
    string ItemKey,
    string ItemType,
    string SubmittedAnswer,
    bool IsCorrect,
    string CorrectAnswer,
    string Explanation,
    decimal Points);
