namespace Api.Grammar.Persistence.Entities;

public sealed class ExerciseQuestionEntity
{
    public Guid Id { get; set; }
    public Guid ExerciseId { get; set; }
    public required string QuestionKey { get; set; }
    public required string Prompt { get; set; }
    public int DisplayOrder { get; set; }
    public decimal Points { get; set; }
    public ExerciseEntity? Exercise { get; set; }
}
