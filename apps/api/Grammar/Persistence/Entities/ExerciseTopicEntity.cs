namespace Api.Grammar.Persistence.Entities;

public sealed class ExerciseTopicEntity
{
    public Guid ExerciseId { get; set; }
    public Guid TopicId { get; set; }
    public ExerciseEntity? Exercise { get; set; }
    public GrammarTopicEntity? Topic { get; set; }
}
