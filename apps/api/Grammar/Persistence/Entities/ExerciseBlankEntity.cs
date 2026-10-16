namespace Api.Grammar.Persistence.Entities;

public sealed class ExerciseBlankEntity
{
    public Guid Id { get; set; }
    public Guid ExerciseId { get; set; }
    public required string BlankKey { get; set; }
    public int DisplayOrder { get; set; }
    public required string InputType { get; set; }
    public decimal Points { get; set; }
    public ExerciseEntity? Exercise { get; set; }
    public ICollection<ExerciseBlankAnswerEntity> Answers { get; set; } = [];
}
