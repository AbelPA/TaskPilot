namespace Api.Grammar.Contracts;

public sealed record GrammarExerciseSubmission(
    IReadOnlyList<GrammarBlankAnswerSubmission>? BlankAnswers,
    IReadOnlyList<GrammarQuestionAnswerSubmission>? QuestionAnswers,
    Guid SubmissionId,
    Guid? TopicId = null,
    Guid? ReviewSessionId = null);

public sealed record GrammarBlankAnswerSubmission(string BlankKey, string Answer);

public sealed record GrammarQuestionAnswerSubmission(string QuestionKey, string OptionKey);
