namespace Api.Grammar.Persistence.Entities;

public sealed class EvaluatedAnswerEntity
{
 public Guid Id { get; set; } public Guid SubmissionId { get; set; } public required string ItemType { get; set; }
 public required string ItemKey { get; set; } public required string SubmittedAnswer { get; set; } public required string CorrectAnswer { get; set; }
 public bool IsCorrect { get; set; } public required string Explanation { get; set; } public decimal Points { get; set; }
 public decimal MaxPoints { get; set; } public GrammarSubmissionEntity? Submission { get; set; }
}
