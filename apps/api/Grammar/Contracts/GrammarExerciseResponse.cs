namespace Api.Grammar.Contracts;

public sealed record GrammarExerciseSummary(
    Guid Id,
    string Title,
    string ExerciseType,
    int DisplayOrder,
    int ItemCount);

public sealed record GrammarExerciseResponse(
    Guid Id,
    string Title,
    string Instruction,
    string ExerciseType,
    string ContentTemplate,
    int ItemCount,
    IReadOnlyList<GrammarBlankResponse> Blanks,
    IReadOnlyList<GrammarQuestionResponse> Questions);

public sealed record GrammarBlankResponse(
    string Key,
    int DisplayOrder,
    string InputType,
    decimal Points,
    IReadOnlyList<GrammarOptionResponse> Options);

public sealed record GrammarQuestionResponse(
    string Key,
    string Prompt,
    int DisplayOrder,
    decimal Points,
    IReadOnlyList<GrammarOptionResponse> Options);

public sealed record GrammarOptionResponse(string Key, string Text, int DisplayOrder);
