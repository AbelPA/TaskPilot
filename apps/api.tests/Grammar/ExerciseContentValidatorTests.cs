using Api.Grammar.Persistence.Entities;
using Api.Grammar.Validation;
using Xunit;

namespace AudioApi.Tests.Grammar;

public sealed class ExerciseContentValidatorTests
{
    [Fact]
    public void Validate_accepts_matching_ordered_markers_and_individual_answers()
    {
        var exercise = CreateExercise();
        exercise.ContentTemplate = "{{blank:1}} you a teacher?\n\nI {{blank:2}}.";
        exercise.Blanks =
        [
            CreateBlank("1", 1, "Are"),
            CreateBlank("2", 2, "am"),
        ];
        exercise.Options =
        [
            CreateBlankOption(exercise.Blanks.First(), "are", true),
            CreateBlankOption(exercise.Blanks.First(), "am", false, displayOrder: 2),
            CreateBlankOption(exercise.Blanks.Last(), "am", true),
            CreateBlankOption(exercise.Blanks.Last(), "are", false, displayOrder: 2),
        ];

        ExerciseContentValidator.Validate(exercise);
    }

    [Theory]
    [InlineData("{{blank:1}} {{blank:1}}", "1")]
    [InlineData("{{blank:2}}", "1")]
    [InlineData("{{blank:1}}", "1,2")]
    [InlineData("no markers", "1")]
    public void Validate_rejects_duplicate_or_unmatched_blank_markers(string template, string keys)
    {
        var exercise = CreateExercise();
        exercise.ContentTemplate = template;
        exercise.Blanks = keys.Split(',')
            .Select((key, index) => CreateBlank(key, index + 1, "answer"))
            .ToList();

        Assert.Throws<GrammarContentException>(() => ExerciseContentValidator.Validate(exercise));
    }

    [Fact]
    public void ParseTheory_rejects_unknown_content_blocks()
    {
        Assert.Throws<GrammarContentException>(() =>
            ExerciseContentValidator.ParseTheory("""[{"type":"html","text":"<script>alert(1)</script>"}]"""));
    }

    [Theory]
    [InlineData("""[{"type":"list","items":["Rule"],"ordered":"yes"}]""")]
    [InlineData("""[{"type":"example","text":"I am.","translation":42}]""")]
    [InlineData("""[{"type":"table","headers":["Subject","Verb"],"rows":[["I"]]}]""")]
    public void ParseTheory_rejects_malformed_typed_block_fields(string content)
    {
        Assert.Throws<GrammarContentException>(() => ExerciseContentValidator.ParseTheory(content));
    }

    [Fact]
    public void Normalize_trims_outer_whitespace_and_obeys_case_setting()
    {
        Assert.Equal("ARE", ExerciseContentValidator.Normalize("  Are ", caseSensitive: false));
        Assert.Equal("Are", ExerciseContentValidator.Normalize("  Are ", caseSensitive: true));
    }

    private static ExerciseEntity CreateExercise()
    {
        var topic = new GrammarTopicEntity
        {
            Id = Guid.NewGuid(),
            Slug = "test-topic",
            Title = "Test",
            Description = "Test",
            Level = "A1",
            IsActive = true,
        };
        var exercise = new ExerciseEntity
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Instruction = "Complete",
            ExerciseType = "fill_blank",
            ContentTemplate = "{{blank:1}}",
            Explanation = "General",
            IsActive = true,
        };
        exercise.ExerciseTopics.Add(new ExerciseTopicEntity
        {
            ExerciseId = exercise.Id,
            TopicId = topic.Id,
            Exercise = exercise,
            Topic = topic,
        });
        return exercise;
    }

    private static ExerciseBlankEntity CreateBlank(string key, int order, string answer)
    {
        var blank = new ExerciseBlankEntity
        {
            Id = Guid.NewGuid(),
            ExerciseId = Guid.Empty,
            BlankKey = key,
            DisplayOrder = order,
            InputType = "select",
            Points = 1,
        };
        blank.Answers.Add(new ExerciseBlankAnswerEntity
        {
            Id = Guid.NewGuid(),
            BlankId = blank.Id,
            Answer = answer,
            IsPrimary = true,
        });
        return blank;
    }

    private static ExerciseOptionEntity CreateBlankOption(
        ExerciseBlankEntity blank,
        string text,
        bool isCorrect,
        int displayOrder = 1) =>
        new()
        {
            Id = Guid.NewGuid(),
            ExerciseId = blank.ExerciseId,
            BlankId = blank.Id,
            OptionKey = text,
            OptionText = text,
            IsCorrect = isCorrect,
            DisplayOrder = displayOrder,
        };
}
