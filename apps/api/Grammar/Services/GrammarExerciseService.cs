using Api.Grammar.Contracts;
using Api.Grammar.Persistence;
using Api.Grammar.Persistence.Entities;
using Api.Grammar.Validation;
using Microsoft.EntityFrameworkCore;

namespace Api.Grammar.Services;

public sealed class GrammarExerciseService(GrammarDbContext dbContext)
{
    public async Task<GrammarExerciseResponse?> GetExerciseAsync(
        Guid exerciseId,
        CancellationToken cancellationToken)
    {
        var exercise = await LoadExerciseAsync(exerciseId, cancellationToken);
        if (exercise is null)
        {
            return null;
        }

        ExerciseContentValidator.Validate(exercise);
        var blanks = exercise.Blanks.OrderBy(blank => blank.DisplayOrder)
            .Select(blank => new GrammarBlankResponse(
                blank.BlankKey,
                blank.DisplayOrder,
                blank.InputType,
                blank.Points,
                exercise.Options.Where(option => option.BlankId == blank.Id)
                    .OrderBy(option => option.DisplayOrder)
                    .Select(ToPublicOption)
                    .ToArray()))
            .ToArray();
        var questions = exercise.Questions.OrderBy(question => question.DisplayOrder)
            .Select(question => new GrammarQuestionResponse(
                question.QuestionKey,
                question.Prompt,
                question.DisplayOrder,
                question.Points,
                exercise.Options.Where(option => option.QuestionId == question.Id)
                    .OrderBy(option => option.DisplayOrder)
                    .Select(ToPublicOption)
                    .ToArray()))
            .ToArray();

        return new GrammarExerciseResponse(
            exercise.Id,
            exercise.Title,
            exercise.Instruction,
            exercise.ExerciseType,
            exercise.ContentTemplate,
            blanks.Length + questions.Length,
            blanks,
            questions);
    }

    public async Task<GrammarExerciseResult?> SubmitAsync(
        Guid exerciseId,
        GrammarExerciseSubmission submission,
        CancellationToken cancellationToken)
    {
        var exercise = await LoadExerciseAsync(exerciseId, cancellationToken);
        if (exercise is null)
        {
            return null;
        }

        ExerciseContentValidator.Validate(exercise);
        var blankAnswers = submission.BlankAnswers ?? [];
        var questionAnswers = submission.QuestionAnswers ?? [];
        EnsureUnique(blankAnswers.Select(answer => answer.BlankKey), "blank");
        EnsureUnique(questionAnswers.Select(answer => answer.QuestionKey), "question");

        if (exercise.ExerciseType == "fill_blank" && questionAnswers.Count != 0 ||
            exercise.ExerciseType == "multiple_choice" && blankAnswers.Count != 0)
        {
            throw new GrammarSubmissionException("Answer types do not match the exercise.");
        }

        var blankByKey = exercise.Blanks.ToDictionary(blank => blank.BlankKey, StringComparer.Ordinal);
        var questionByKey = exercise.Questions.ToDictionary(question => question.QuestionKey, StringComparer.Ordinal);
        if (blankAnswers.Any(answer => !blankByKey.ContainsKey(answer.BlankKey)) ||
            questionAnswers.Any(answer => !questionByKey.ContainsKey(answer.QuestionKey)))
        {
            throw new GrammarSubmissionException("One or more submitted answer keys do not belong to this exercise.");
        }

        var optionByQuestionAndKey = exercise.Options
            .Where(option => option.QuestionId is not null)
            .ToDictionary(option => (option.QuestionId!.Value, option.OptionKey));
        foreach (var answer in questionAnswers)
        {
            var question = questionByKey[answer.QuestionKey];
            if (!optionByQuestionAndKey.ContainsKey((question.Id, answer.OptionKey)))
            {
                throw new GrammarSubmissionException("A selected option does not belong to its question.");
            }
        }

        var blankAnswerByKey = blankAnswers.ToDictionary(answer => answer.BlankKey, StringComparer.Ordinal);
        var questionAnswerByKey = questionAnswers.ToDictionary(answer => answer.QuestionKey, StringComparer.Ordinal);
        var results = new List<GrammarExerciseItemResult>();

        foreach (var blank in exercise.Blanks.OrderBy(blank => blank.DisplayOrder))
        {
            blankAnswerByKey.TryGetValue(blank.BlankKey, out var submitted);
            var response = submitted?.Answer ?? string.Empty;
            var isCorrect = blank.Answers.Any(answer =>
                ExerciseContentValidator.Normalize(answer.Answer, exercise.CaseSensitive) ==
                ExerciseContentValidator.Normalize(response, exercise.CaseSensitive));
            var acceptedAnswer = blank.Answers.FirstOrDefault(answer => answer.IsPrimary) ??
                blank.Answers.OrderBy(answer => answer.Answer).First();
            var explanation = blank.Answers.FirstOrDefault(answer => isCorrect &&
                ExerciseContentValidator.Normalize(answer.Answer, exercise.CaseSensitive) ==
                ExerciseContentValidator.Normalize(response, exercise.CaseSensitive))?.Explanation;
            results.Add(new GrammarExerciseItemResult(
                blank.BlankKey,
                "blank",
                response,
                isCorrect,
                acceptedAnswer.Answer,
                explanation ?? exercise.Explanation,
                isCorrect ? blank.Points : 0));
        }

        foreach (var question in exercise.Questions.OrderBy(question => question.DisplayOrder))
        {
            questionAnswerByKey.TryGetValue(question.QuestionKey, out var submitted);
            var selected = submitted is null
                ? null
                : optionByQuestionAndKey[(question.Id, submitted.OptionKey)];
            var correct = exercise.Options.Single(option =>
                option.QuestionId == question.Id && option.IsCorrect);
            results.Add(new GrammarExerciseItemResult(
                question.QuestionKey,
                "question",
                selected is null ? string.Empty : $"{selected.OptionKey}) {selected.OptionText}",
                selected?.Id == correct.Id,
                $"{correct.OptionKey}) {correct.OptionText}",
                selected?.Explanation ?? correct.Explanation ?? exercise.Explanation,
                selected?.Id == correct.Id ? question.Points : 0));
        }

        var maxScore = results.Sum(result => result.ItemType == "blank"
            ? blankByKey[result.ItemKey].Points
            : questionByKey[result.ItemKey].Points);
        var score = results.Sum(result => result.Points);
        var percentage = Math.Round(score / maxScore * 100m, 1, MidpointRounding.AwayFromZero);

        return new GrammarExerciseResult(
            exercise.Id,
            score,
            maxScore,
            percentage,
            exercise.Explanation,
            results);
    }

    private async Task<ExerciseEntity?> LoadExerciseAsync(Guid exerciseId, CancellationToken cancellationToken) =>
        await dbContext.Exercises
            .AsNoTracking()
            .AsSplitQuery()
            .Where(exercise => exercise.Id == exerciseId && exercise.IsActive)
            .Include(exercise => exercise.ExerciseTopics)
                .ThenInclude(link => link.Topic)
            .Include(exercise => exercise.Blanks)
                .ThenInclude(blank => blank.Answers)
            .Include(exercise => exercise.Questions)
            .Include(exercise => exercise.Options)
            .SingleOrDefaultAsync(cancellationToken);

    private static GrammarOptionResponse ToPublicOption(ExerciseOptionEntity option) =>
        new(option.OptionKey, option.OptionText, option.DisplayOrder);

    private static void EnsureUnique(IEnumerable<string> keys, string kind)
    {
        var values = keys.ToArray();
        if (values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new GrammarSubmissionException($"Submitted {kind} keys must be non-empty and unique.");
        }
    }
}

public sealed class GrammarSubmissionException(string message) : Exception(message);
