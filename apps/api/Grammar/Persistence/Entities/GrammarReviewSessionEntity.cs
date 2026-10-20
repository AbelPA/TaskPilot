namespace Api.Grammar.Persistence.Entities;

public sealed class GrammarReviewSessionEntity
{
 public Guid Id { get; set; } public DateTimeOffset CreatedAt { get; set; } public int SelectionSeed { get; set; }
 public int RequestedExerciseCount { get; set; } public int AvailableExerciseCount { get; set; }
 public ICollection<GrammarReviewExerciseEntity> Exercises { get; set; } = [];
}
