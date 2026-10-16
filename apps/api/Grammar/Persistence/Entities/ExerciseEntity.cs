namespace Api.Grammar.Persistence.Entities;

public sealed class ExerciseEntity
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Instruction { get; set; }
    public required string ExerciseType { get; set; }
    public required string ContentTemplate { get; set; }
    public required string Explanation { get; set; }
    public bool CaseSensitive { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<ExerciseTopicEntity> ExerciseTopics { get; set; } = [];
    public ICollection<ExerciseBlankEntity> Blanks { get; set; } = [];
    public ICollection<ExerciseQuestionEntity> Questions { get; set; } = [];
    public ICollection<ExerciseOptionEntity> Options { get; set; } = [];
}
