namespace Api.Grammar.Persistence.Entities;

public sealed class GrammarTopicEntity
{
    public Guid Id { get; set; }
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Level { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<GrammarTheorySectionEntity> TheorySections { get; set; } = [];
    public ICollection<ExerciseTopicEntity> ExerciseTopics { get; set; } = [];
}
