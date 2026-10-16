using Api.Grammar.Contracts;
using Api.Grammar.Persistence;
using Api.Grammar.Persistence.Entities;
using Api.Grammar.Validation;
using Microsoft.EntityFrameworkCore;

namespace Api.Grammar.Services;

public sealed class GrammarCatalogService(GrammarDbContext dbContext)
{
    public async Task<IReadOnlyList<GrammarTopicSummary>> GetTopicsAsync(CancellationToken cancellationToken)
    {
        var topics = await dbContext.Topics
            .AsNoTracking()
            .Where(topic => topic.IsActive)
            .OrderBy(topic => topic.DisplayOrder)
            .ThenBy(topic => topic.Title)
            .Select(topic => new
            {
                topic.Id,
                topic.Slug,
                topic.Title,
                topic.Description,
                topic.Level,
                topic.DisplayOrder,
                ExerciseCount = topic.ExerciseTopics.Count(link =>
                    link.Exercise != null && link.Exercise.IsActive),
            })
            .ToListAsync(cancellationToken);

        return topics.Select(topic => new GrammarTopicSummary(
            topic.Id,
            topic.Slug,
            topic.Title,
            topic.Description,
            topic.Level,
            topic.DisplayOrder,
            topic.ExerciseCount)).ToArray();
    }

    public async Task<GrammarTopicResponse?> GetTopicAsync(string slug, CancellationToken cancellationToken)
    {
        var topics = await dbContext.Topics
            .AsNoTracking()
            .Where(topic => topic.IsActive)
            .OrderBy(topic => topic.DisplayOrder)
            .ThenBy(topic => topic.Title)
            .Select(topic => new
            {
                topic.Id,
                topic.Slug,
                topic.Title,
                topic.Description,
                topic.Level,
                topic.DisplayOrder,
                ExerciseCount = topic.ExerciseTopics.Count(link =>
                    link.Exercise != null && link.Exercise.IsActive),
            })
            .ToListAsync(cancellationToken);
        var index = topics.FindIndex(topic => topic.Slug == slug);
        if (index < 0)
        {
            return null;
        }

        var topic = topics[index];
        var sections = await dbContext.TheorySections
            .AsNoTracking()
            .Where(section => section.TopicId == topic.Id)
            .OrderBy(section => section.DisplayOrder)
            .Select(section => new
            {
                section.Title,
                section.DisplayOrder,
                section.ContentJson,
            })
            .ToListAsync(cancellationToken);

        var sectionResponses = sections.Select(section => new GrammarTheorySectionResponse(
            section.Title,
            section.DisplayOrder,
            ExerciseContentValidator.ParseTheory(section.ContentJson))).ToArray();

        return new GrammarTopicResponse(
            topic.Id,
            topic.Slug,
            topic.Title,
            topic.Description,
            topic.Level,
            topic.DisplayOrder,
            topic.ExerciseCount,
            index > 0 ? topics[index - 1].Slug : null,
            index + 1 < topics.Count ? topics[index + 1].Slug : null,
            sectionResponses);
    }

    public async Task<IReadOnlyList<GrammarExerciseSummary>?> GetExercisesAsync(
        string topicSlug,
        CancellationToken cancellationToken)
    {
        var topicId = await dbContext.Topics
            .AsNoTracking()
            .Where(topic => topic.IsActive && topic.Slug == topicSlug)
            .Select(topic => (Guid?)topic.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (topicId is null)
        {
            return null;
        }

        var exercises = await dbContext.Exercises
            .AsNoTracking()
            .Where(exercise => exercise.IsActive &&
                exercise.ExerciseTopics.Any(link => link.TopicId == topicId))
            .OrderBy(exercise => exercise.DisplayOrder)
            .ThenBy(exercise => exercise.Title)
            .Select(exercise => new
            {
                exercise.Id,
                exercise.Title,
                exercise.ExerciseType,
                exercise.DisplayOrder,
                ItemCount = exercise.ExerciseType == "fill_blank"
                    ? exercise.Blanks.Count
                    : exercise.Questions.Count,
            })
            .ToListAsync(cancellationToken);

        return exercises.Select(exercise => new GrammarExerciseSummary(
            exercise.Id,
            exercise.Title,
            exercise.ExerciseType,
            exercise.DisplayOrder,
            exercise.ItemCount)).ToArray();
    }
}
