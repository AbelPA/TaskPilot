namespace Api.Grammar.Persistence.Entities;

public sealed class GrammarReviewExerciseEntity
{
 public Guid Id { get; set; } public Guid SessionId { get; set; } public Guid ExerciseId { get; set; } public Guid TopicId { get; set; }
 public required string SelectionCategory { get; set; } public decimal? PriorityScore { get; set; } public required string Reason { get; set; }
 public int DisplayOrder { get; set; } public required string TitleSnapshot { get; set; } public required string TopicTitleSnapshot { get; set; }
 public GrammarReviewSessionEntity? Session { get; set; }
}
