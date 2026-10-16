namespace Api.Grammar.Persistence.Entities;

public sealed class ExerciseOptionEntity
{
    public Guid Id { get; set; }
    public Guid ExerciseId { get; set; }
    public Guid? QuestionId { get; set; }
    public Guid? BlankId { get; set; }
    public required string OptionKey { get; set; }
    public required string OptionText { get; set; }
    public bool IsCorrect { get; set; }
    public string? Explanation { get; set; }
    public int DisplayOrder { get; set; }
    public ExerciseEntity? Exercise { get; set; }
    public ExerciseQuestionEntity? Question { get; set; }
    public ExerciseBlankEntity? Blank { get; set; }
}
