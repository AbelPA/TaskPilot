namespace Api.Grammar.Persistence.Entities;

public sealed class GrammarSubmissionEntity
{
 public Guid Id { get; set; } public Guid ExerciseId { get; set; } public required string ExerciseTitleSnapshot { get; set; }
 public Guid TopicId { get; set; } public required string TopicTitleSnapshot { get; set; } public Guid? ReviewSessionId { get; set; }
 public int AttemptNumber { get; set; } public DateTimeOffset EvaluatedAt { get; set; } public decimal Score { get; set; }
 public decimal MaxScore { get; set; } public decimal Percentage { get; set; } public required string ExerciseExplanation { get; set; }
 public required string PayloadHash { get; set; } public ICollection<EvaluatedAnswerEntity> Answers { get; set; } = [];
}
