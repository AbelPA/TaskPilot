namespace Api.Grammar.Contracts;

public sealed record GrammarExerciseSubmission(
    IReadOnlyList<GrammarBlankAnswerSubmission>? BlankAnswers,
    IReadOnlyList<GrammarQuestionAnswerSubmission>? QuestionAnswers);

public sealed record GrammarBlankAnswerSubmission(string BlankKey, string Answer);

public sealed record GrammarQuestionAnswerSubmission(string QuestionKey, string OptionKey);
