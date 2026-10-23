using System.Reflection;
using System.Text.Json;
using Api.Grammar.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Grammar.Persistence;

internal static class GrammarSeedData
{
    private const string ResourceName = "Api.Grammar.Persistence.SeedData.a1-a2-grammar.json";

    public static async Task SeedAsync(GrammarDbContext dbContext, CancellationToken cancellationToken)
    {
        var seed = ReadSeed();

        var existingTopicRows = await dbContext.Topics
            .ToDictionaryAsync(topic => topic.Slug, cancellationToken);
        var existingTopics = existingTopicRows.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
        var topicIdMap = seed.Topics.ToDictionary(
            topic => topic.Id,
            topic => existingTopics.GetValueOrDefault(topic.Slug, topic.Id));
        foreach (var topic in seed.Topics)
        {
            if (existingTopicRows.TryGetValue(topic.Slug, out var existingTopic))
            {
                var topicChanged = existingTopic.Title != topic.Title ||
                                   existingTopic.Description != topic.Description ||
                                   existingTopic.Level != topic.Level ||
                                   existingTopic.DisplayOrder != topic.DisplayOrder ||
                                   existingTopic.IsActive != topic.IsActive;
                existingTopic.Title = topic.Title;
                existingTopic.Description = topic.Description;
                existingTopic.Level = topic.Level;
                existingTopic.DisplayOrder = topic.DisplayOrder;
                existingTopic.IsActive = topic.IsActive;
                if (topicChanged)
                {
                    existingTopic.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                dbContext.Topics.Add(topic);
            }
        }

        var existingTheorySections = await dbContext.TheorySections
            .ToDictionaryAsync(section => section.Id, cancellationToken);
        foreach (var section in seed.TheorySections)
        {
            section.TopicId = topicIdMap[section.TopicId];
            if (existingTheorySections.TryGetValue(section.Id, out var existingSection))
            {
                existingSection.TopicId = section.TopicId;
                existingSection.Title = section.Title;
                existingSection.ContentJson = section.ContentJson;
                existingSection.DisplayOrder = section.DisplayOrder;
            }
            else
            {
                dbContext.TheorySections.Add(section);
            }
        }

        var existingExerciseIds = await dbContext.Exercises.AsNoTracking()
            .Select(exercise => exercise.Id).ToHashSetAsync(cancellationToken);
        dbContext.Exercises.AddRange(seed.Exercises.Where(exercise => !existingExerciseIds.Contains(exercise.Id)));

        var existingExerciseTopics = await dbContext.ExerciseTopics.AsNoTracking()
            .Select(link => new { link.ExerciseId, link.TopicId })
            .ToListAsync(cancellationToken);
        var existingLinkKeys = existingExerciseTopics
            .Select(link => (link.ExerciseId, link.TopicId)).ToHashSet();
        dbContext.ExerciseTopics.AddRange(seed.ExerciseTopics
            .Where(link => !existingLinkKeys.Contains((link.ExerciseId, topicIdMap[link.TopicId])))
            .Select(link => new ExerciseTopicEntity
            {
                ExerciseId = link.ExerciseId,
                TopicId = topicIdMap[link.TopicId],
            }));

        AddMissingById(seed.Blanks,
            await dbContext.Blanks.AsNoTracking().Select(blank => blank.Id).ToHashSetAsync(cancellationToken),
            dbContext.Blanks,
            blank => blank.Id);
        AddMissingById(seed.BlankAnswers,
            await dbContext.BlankAnswers.AsNoTracking().Select(answer => answer.Id).ToHashSetAsync(cancellationToken),
            dbContext.BlankAnswers,
            answer => answer.Id);
        AddMissingById(seed.Questions,
            await dbContext.Questions.AsNoTracking().Select(question => question.Id).ToHashSetAsync(cancellationToken),
            dbContext.Questions,
            question => question.Id);
        AddMissingById(seed.Options,
            await dbContext.Options.AsNoTracking().Select(option => option.Id).ToHashSetAsync(cancellationToken),
            dbContext.Options,
            option => option.Id);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void AddMissingById<TEntity>(
        IEnumerable<TEntity> seeds,
        HashSet<Guid> existingIds,
        DbSet<TEntity> set,
        Func<TEntity, Guid> idSelector) where TEntity : class
    {
        set.AddRange(seeds.Where(seed => !existingIds.Contains(idSelector(seed))));
    }

    private static GrammarSeedDocument ReadSeed()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Seed resource '{ResourceName}' was not found.");
        return JsonSerializer.Deserialize<GrammarSeedDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Grammar seed data is empty.");
    }

    private sealed class GrammarSeedDocument
    {
        public List<GrammarTopicEntity> Topics { get; init; } = [];
        public List<GrammarTheorySectionEntity> TheorySections { get; init; } = [];
        public List<ExerciseEntity> Exercises { get; init; } = [];
        public List<ExerciseTopicEntity> ExerciseTopics { get; init; } = [];
        public List<ExerciseBlankEntity> Blanks { get; init; } = [];
        public List<ExerciseBlankAnswerEntity> BlankAnswers { get; init; } = [];
        public List<ExerciseQuestionEntity> Questions { get; init; } = [];
        public List<ExerciseOptionEntity> Options { get; init; } = [];
    }
}
