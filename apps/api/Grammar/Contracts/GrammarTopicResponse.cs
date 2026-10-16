namespace Api.Grammar.Contracts;

public sealed record GrammarTopicSummary(
    Guid Id,
    string Slug,
    string Title,
    string Description,
    string Level,
    int DisplayOrder,
    int ExerciseCount);

public sealed record GrammarTopicResponse(
    Guid Id,
    string Slug,
    string Title,
    string Description,
    string Level,
    int DisplayOrder,
    int ExerciseCount,
    string? PreviousTopicSlug,
    string? NextTopicSlug,
    IReadOnlyList<GrammarTheorySectionResponse> TheorySections);

public sealed record GrammarTheorySectionResponse(
    string Title,
    int DisplayOrder,
    IReadOnlyList<GrammarTheoryBlock> Blocks);

public sealed record GrammarTheoryBlock(
    string Type,
    string? Text = null,
    string? Translation = null,
    bool? Ordered = null,
    IReadOnlyList<string>? Items = null,
    IReadOnlyList<string>? Headers = null,
    IReadOnlyList<IReadOnlyList<string>>? Rows = null);
