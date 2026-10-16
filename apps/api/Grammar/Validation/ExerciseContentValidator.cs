using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Grammar.Persistence.Entities;

namespace Api.Grammar.Validation;

public static partial class ExerciseContentValidator
{
    [GeneratedRegex(@"\{\{blank:([^{}]+)\}\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex BlankMarkerRegex();

    public static void Validate(ExerciseEntity exercise)
    {
        if (exercise.ExerciseTopics.Count == 0 ||
            exercise.ExerciseTopics.Any(link => link.Topic is not { IsActive: true }))
        {
            throw new GrammarContentException("Exercise must be linked to at least one active topic.");
        }

        if (exercise.ExerciseType == "fill_blank")
        {
            ValidateBlanks(exercise);
            return;
        }

        if (exercise.ExerciseType == "multiple_choice")
        {
            ValidateQuestions(exercise);
            return;
        }

        throw new GrammarContentException("Exercise type is not supported.");
    }

    private static void ValidateBlanks(ExerciseEntity exercise)
    {
        var markers = BlankMarkerRegex().Matches(exercise.ContentTemplate)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        var keys = exercise.Blanks.OrderBy(blank => blank.DisplayOrder)
            .Select(blank => blank.BlankKey)
            .ToArray();

        if (string.IsNullOrWhiteSpace(exercise.ContentTemplate) ||
            markers.Length == 0 ||
            markers.Distinct(StringComparer.Ordinal).Count() != markers.Length ||
            !markers.SequenceEqual(keys, StringComparer.Ordinal))
        {
            throw new GrammarContentException("Exercise blank markers do not match its configured blanks.");
        }

        foreach (var blank in exercise.Blanks)
        {
            if (blank.Points <= 0 ||
                blank.Answers.Count == 0 ||
                blank.Answers.Any(answer => string.IsNullOrWhiteSpace(answer.Answer)) ||
                blank.Answers.Count(answer => answer.IsPrimary) > 1)
            {
                throw new GrammarContentException($"Blank '{blank.BlankKey}' has invalid accepted answers.");
            }

            if (blank.InputType is not ("text" or "select"))
            {
                throw new GrammarContentException($"Blank '{blank.BlankKey}' has an unsupported input type.");
            }

            var options = exercise.Options.Where(option => option.BlankId == blank.Id).ToArray();
            if (blank.InputType == "select" &&
                (options.Length < 2 ||
                 options.Count(option => option.IsCorrect) != 1 ||
                 options.Any(option => string.IsNullOrWhiteSpace(option.OptionKey) ||
                                       string.IsNullOrWhiteSpace(option.OptionText))))
            {
                throw new GrammarContentException($"Select blank '{blank.BlankKey}' has invalid options.");
            }

            if (blank.InputType == "text" && options.Length != 0)
            {
                throw new GrammarContentException($"Text blank '{blank.BlankKey}' cannot have options.");
            }

            if (options.Any(option => option.IsCorrect &&
                !blank.Answers.Any(answer => Normalize(answer.Answer, exercise.CaseSensitive) ==
                                             Normalize(option.OptionText, exercise.CaseSensitive))))
            {
                throw new GrammarContentException($"Select blank '{blank.BlankKey}' has an option not in its accepted answers.");
            }
        }

        if (exercise.Questions.Count != 0 ||
            exercise.Options.Any(option => option.QuestionId is not null))
        {
            throw new GrammarContentException("Fill-blank exercises cannot contain multiple-choice questions.");
        }
    }

    private static void ValidateQuestions(ExerciseEntity exercise)
    {
        if (exercise.Questions.Count == 0 || exercise.Blanks.Count != 0)
        {
            throw new GrammarContentException("Multiple-choice exercises require questions and cannot contain blanks.");
        }

        foreach (var question in exercise.Questions)
        {
            var options = exercise.Options.Where(option => option.QuestionId == question.Id).ToArray();
            if (question.Points <= 0 ||
                options.Length < 2 ||
                options.Count(option => option.IsCorrect) != 1 ||
                options.Any(option => string.IsNullOrWhiteSpace(option.OptionKey) ||
                                     string.IsNullOrWhiteSpace(option.OptionText)))
            {
                throw new GrammarContentException($"Question '{question.QuestionKey}' must have at least two options and exactly one correct option.");
            }
        }

        if (exercise.Options.Any(option => option.BlankId is not null))
        {
            throw new GrammarContentException("Multiple-choice exercises cannot contain blank options.");
        }
    }

    public static IReadOnlyList<Contracts.GrammarTheoryBlock> ParseTheory(string contentJson)
    {
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new GrammarContentException("Theory content must be an array of typed blocks.");
            }

            return document.RootElement.EnumerateArray().Select(ParseBlock).ToArray();
        }
        catch (JsonException exception)
        {
            throw new GrammarContentException($"Theory content contains invalid JSON: {exception.Message}");
        }
    }

    private static Contracts.GrammarTheoryBlock ParseBlock(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            throw new GrammarContentException("Theory block must have a type.");
        }

        var type = typeElement.GetString();
        return type switch
        {
            "paragraph" => new(type, RequiredString(element, "text")),
            "note" => new(type, RequiredString(element, "text")),
            "example" => new(type, RequiredString(element, "text"),
                OptionalString(element, "translation")),
            "list" => new(type, Ordered: OptionalBoolean(element, "ordered") ?? false,
                Items: RequiredStringArray(element, "items")),
            "table" => ParseTableBlock(element),
            _ => throw new GrammarContentException($"Theory block type '{type}' is not supported."),
        };
    }

    private static Contracts.GrammarTheoryBlock ParseTableBlock(JsonElement element)
    {
        var headers = RequiredStringArray(element, "headers");
        var rows = RequiredStringRows(element, "rows");
        if (headers.Count == 0 || rows.Any(row => row.Count != headers.Count))
        {
            throw new GrammarContentException("Theory table rows must match its non-empty header.");
        }

        return new("table", Headers: headers, Rows: rows);
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new GrammarContentException($"Theory block requires '{propertyName}'.");

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : throw new GrammarContentException($"Theory block '{propertyName}' must be text or null.");
    }

    private static bool? OptionalBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : throw new GrammarContentException($"Theory block '{propertyName}' must be boolean.");
    }

    private static IReadOnlyList<string> RequiredStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            throw new GrammarContentException($"Theory block requires '{propertyName}' array.");
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new GrammarContentException($"Theory block '{propertyName}' must contain only strings."))
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<string>> RequiredStringRows(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            throw new GrammarContentException($"Theory block requires '{propertyName}' array.");
        }

        return property.EnumerateArray().Select(row =>
        {
            if (row.ValueKind != JsonValueKind.Array)
            {
                throw new GrammarContentException($"Theory block '{propertyName}' must contain arrays.");
            }

            return (IReadOnlyList<string>)row.EnumerateArray()
                .Select(cell => cell.ValueKind == JsonValueKind.String
                    ? cell.GetString()!
                    : throw new GrammarContentException($"Theory block '{propertyName}' cells must be strings."))
                .ToArray();
        }).ToArray();
    }

    public static string Normalize(string value, bool caseSensitive)
    {
        var trimmed = value.Trim();
        return caseSensitive ? trimmed : trimmed.ToUpperInvariant();
    }
}
