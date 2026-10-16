namespace Api.Grammar.Persistence.Entities;

public sealed class ExerciseBlankAnswerEntity
{
    public Guid Id { get; set; }
    public Guid BlankId { get; set; }
    public required string Answer { get; set; }
    public bool IsPrimary { get; set; }
    public string? Explanation { get; set; }
    public ExerciseBlankEntity? Blank { get; set; }
}
