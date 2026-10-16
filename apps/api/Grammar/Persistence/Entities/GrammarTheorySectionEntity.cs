namespace Api.Grammar.Persistence.Entities;

public sealed class GrammarTheorySectionEntity
{
    public Guid Id { get; set; }
    public Guid TopicId { get; set; }
    public required string Title { get; set; }
    public required string ContentJson { get; set; }
    public int DisplayOrder { get; set; }
    public GrammarTopicEntity? Topic { get; set; }
}
